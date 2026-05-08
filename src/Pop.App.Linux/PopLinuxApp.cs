using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using System.Runtime.InteropServices;
using Pop.Core.Models;

namespace Pop.App.Linux;

public sealed class PopLinuxApp : Application
{
    private LinuxPopHost? _host;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;

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
            desktop.Exit += (_, _) => _host?.Dispose();
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
                    quitItem
                }
            },
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowSettingsWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void ShowSettingsWindow()
    {
        if (_host is null)
        {
            return;
        }

        _settingsWindow ??= new SettingsWindow(_host.Settings, SaveSettingsAsync);
        _settingsWindow.ShowOrBringToFront(_host.Settings);
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
