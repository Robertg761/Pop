using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Pop.App.Linux.Services;
using Pop.Core.Models;

namespace Pop.App.Linux;

public sealed class PopLinuxApp : Application
{
    private readonly UpdateService _updateService;
    private LinuxPopHost? _host;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _updateStatusMenuItem;
    private NativeMenuItem? _checkForUpdatesMenuItem;
    private NativeMenuItem? _installUpdateMenuItem;
    private UpdateState _lastUpdateState;
    private string? _lastNotifiedReadyVersion;

    public PopLinuxApp()
    {
        _updateService = new UpdateService(shutdownHandler: new DelegateAppShutdownHandler(Shutdown));
        _lastUpdateState = _updateService.CurrentState;
    }

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                _updateService.Dispose();
                _host?.Dispose();
            };
        }

        InitializeAsync();
        base.OnFrameworkInitializationCompleted();
    }

    private async void InitializeAsync()
    {
        try
        {
            _host = new LinuxPopHost();
            await _host.InitializeAsync();
            ConfigureTrayIcon();
            ConfigureUpdates();
            await _updateService.StartAsync();
            Console.WriteLine("Pop for Linux is running.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Pop couldn't start: {exception.Message}");
            Shutdown();
        }
    }

    private void ConfigureTrayIcon()
    {
        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) => ShowSettingsWindow();

        _updateStatusMenuItem = new NativeMenuItem("Updates: Starting...")
        {
            IsEnabled = false
        };

        _checkForUpdatesMenuItem = new NativeMenuItem("Check for Updates");
        _checkForUpdatesMenuItem.Click += async (_, _) => await CheckForUpdatesAsync();

        _installUpdateMenuItem = new NativeMenuItem("Install Update")
        {
            IsVisible = false
        };
        _installUpdateMenuItem.Click += (_, _) => _updateService.ApplyPendingUpdateAndRestart();

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Shutdown();

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Pop",
            Icon = LoadTrayIcon(),
            Menu = new NativeMenu
            {
                Items =
                {
                    settingsItem,
                    new NativeMenuItemSeparator(),
                    _updateStatusMenuItem,
                    _checkForUpdatesMenuItem,
                    _installUpdateMenuItem,
                    new NativeMenuItemSeparator(),
                    quitItem
                }
            },
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowSettingsWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void ConfigureUpdates()
    {
        _updateService.StateChanged += OnUpdateStateChanged;
        ApplyUpdateState(_updateService.CurrentState);
    }

    private void ShowSettingsWindow()
    {
        if (_host is null)
        {
            return;
        }

        _settingsWindow ??= new SettingsWindow(_host.Settings, _updateService, SaveSettingsAsync);
        _settingsWindow.ShowOrBringToFront(_host.Settings);
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await _updateService.CheckNowAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Pop update check failed: {exception.Message}");
        }
    }

    private async Task<bool> SaveSettingsAsync(AppSettings settings)
    {
        if (_host is null)
        {
            return false;
        }

        await _host.SaveSettingsAsync(settings);
        return true;
    }

    private void OnUpdateStateChanged(object? sender, UpdateStateChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyUpdateState(e.State);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyUpdateState(e.State));
    }

    private void ApplyUpdateState(UpdateState state)
    {
        var previousState = _lastUpdateState;
        _lastUpdateState = state;

        if (_updateStatusMenuItem is not null)
        {
            _updateStatusMenuItem.Header = GetUpdateMenuText(state);
        }

        if (_checkForUpdatesMenuItem is not null)
        {
            _checkForUpdatesMenuItem.IsEnabled = state.CanCheck || state.Status == UpdateStatus.Unsupported;
        }

        if (_installUpdateMenuItem is not null)
        {
            _installUpdateMenuItem.IsVisible = state.CanInstall;
            _installUpdateMenuItem.IsEnabled = state.CanInstall;
            _installUpdateMenuItem.Header = state.CanInstall && !string.IsNullOrWhiteSpace(state.AvailableVersion)
                ? $"Install Update v{state.AvailableVersion}"
                : "Install Update";
        }

        if (state.Status == UpdateStatus.ReadyToInstall
            && !string.Equals(_lastNotifiedReadyVersion, state.AvailableVersion, StringComparison.Ordinal)
            && previousState.Status != UpdateStatus.ReadyToInstall)
        {
            _lastNotifiedReadyVersion = state.AvailableVersion;
            ShowUpdateReadyNotification(state);
        }

        if (state.Status != UpdateStatus.ReadyToInstall)
        {
            _lastNotifiedReadyVersion = null;
        }
    }

    private static string GetUpdateMenuText(UpdateState state)
    {
        return state.Status switch
        {
            UpdateStatus.Downloading when state.DownloadProgressPercent is int progress =>
                $"Updates: Downloading {progress}%",
            UpdateStatus.ReadyToInstall when !string.IsNullOrWhiteSpace(state.AvailableVersion) =>
                $"Updates: Ready to install v{state.AvailableVersion}",
            _ => $"Updates: {state.Message}"
        };
    }

    private static void ShowUpdateReadyNotification(UpdateState state)
    {
        var message = string.IsNullOrWhiteSpace(state.AvailableVersion)
            ? "Restart Pop to finish installing the downloaded update."
            : $"Restart Pop to install v{state.AvailableVersion}.";

        try
        {
            var startInfo = new ProcessStartInfo("notify-send")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("Pop update ready");
            startInfo.ArgumentList.Add(message);
            Process.Start(startInfo);
        }
        catch
        {
            Console.WriteLine($"Pop update ready. {message}");
        }
    }

    public static WindowIcon? LoadTrayIcon()
    {
        try
        {
            return CreateTrayIcon();
        }
        catch
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://Pop/Assets/official_icon.png"));
                return new WindowIcon(new Bitmap(stream));
            }
            catch
            {
                return null;
            }
        }
    }

    private static WindowIcon CreateTrayIcon()
    {
        const int size = 64;
        var bitmap = new WriteableBitmap(
            new PixelSize(size, size),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var frameBuffer = bitmap.Lock())
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - 32;
                    var dy = y - 32;
                    var isCircle = (dx * dx) + (dy * dy) <= 28 * 28;
                    var isGlyph =
                        (x >= 21 && x <= 28 && y >= 16 && y <= 48) ||
                        (x >= 28 && x <= 43 && y >= 16 && y <= 23) ||
                        (x >= 28 && x <= 43 && y >= 33 && y <= 40) ||
                        (x >= 42 && x <= 49 && y >= 23 && y <= 33);

                    var color = isGlyph
                        ? unchecked((int)0xFFFFFFFF)
                        : isCircle
                            ? unchecked((int)0xFFFF7A1A)
                            : 0;

                    Marshal.WriteInt32(frameBuffer.Address + (y * frameBuffer.RowBytes) + (x * 4), color);
                }
            }
        }

        return new WindowIcon(bitmap);
    }

    private void Shutdown()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
