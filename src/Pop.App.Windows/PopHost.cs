using System.Drawing;
using Pop.App.Windows.Platform.Input;
using Pop.App.Windows.Platform.Startup;
using Pop.App.Windows.Platform.Windowing;
using Pop.App.Windows.Services;
using Pop.Core.Events;
using Pop.Core.Interfaces;
using Pop.Core.Models;
using Pop.Core.Services;
using Pop.Platform.Abstractions.Input;
using Pop.Platform.Abstractions.Startup;
using Pop.Platform.Abstractions.Windowing;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace Pop.App.Windows;

public sealed class PopHost : IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IUpdateService _updateService;
    private readonly IDragTracker _dragTracker;
    private readonly IWindowInspector _windowInspector;
    private readonly ISnapDecider _snapDecider;
    private readonly IWindowSnapBoundsCalculator _snapBoundsCalculator;
    private readonly IWindowMover _windowMover;
    private readonly WindowAnimator _windowAnimator;
    private readonly DiagnosticsLogService _diagnosticsLogService = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly Forms.ToolStripMenuItem _enabledMenuItem;
    private readonly Forms.ToolStripMenuItem _launchAtStartupMenuItem;
    private readonly Forms.ToolStripMenuItem _versionMenuItem;
    private readonly Forms.ToolStripMenuItem _updateStatusMenuItem;
    private readonly Forms.ToolStripMenuItem _checkForUpdatesMenuItem;
    private readonly Forms.ToolStripMenuItem _installUpdateMenuItem;

    private AppSettings _settings = new();
    private SettingsWindow? _settingsWindow;
    private UpdateState _lastUpdateState;
    private string? _lastNotifiedReadyVersion;

    public PopHost()
    {
        _settingsStore = new JsonSettingsStore();
        _startupRegistration = new WindowsStartupRegistration();
        _updateService = new UpdateService();
        _windowInspector = new WindowInspector(new WindowEligibilityEvaluator());
        _snapDecider = new SnapDecider(_windowInspector.InspectMonitorAt);
        _snapBoundsCalculator = new WindowSnapBoundsCalculator();
        _windowMover = new Win32WindowMover();
        _windowAnimator = new WindowAnimator();

        _dragTracker = new MouseHookDragTracker(_windowInspector);
        _dragTracker.DragRejected += OnDragRejected;
        _dragTracker.DragStarted += OnDragStarted;
        _dragTracker.DragUpdated += OnDragUpdated;
        _dragTracker.DragCompleted += OnDragCompleted;

        _enabledMenuItem = new Forms.ToolStripMenuItem("Enable Pop", null, async (_, _) => await ToggleEnabledAsync());
        _launchAtStartupMenuItem = new Forms.ToolStripMenuItem("Launch At Startup", null, async (_, _) => await ToggleLaunchAtStartupAsync());
        _versionMenuItem = new Forms.ToolStripMenuItem($"Version {AppReleaseMetadata.CurrentVersion}")
        {
            Enabled = false
        };
        _updateStatusMenuItem = new Forms.ToolStripMenuItem("Updates: Starting...")
        {
            Enabled = false
        };
        _checkForUpdatesMenuItem = new Forms.ToolStripMenuItem("Check For Updates", null, async (_, _) => await CheckForUpdatesAsync());
        _installUpdateMenuItem = new Forms.ToolStripMenuItem("Install Update", null, (_, _) => _updateService.ApplyPendingUpdateAndRestart())
        {
            Visible = false
        };

        var openSettingsMenuItem = new Forms.ToolStripMenuItem("Open Settings", null, (_, _) => OpenSettingsWindow());
        var exitMenuItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown());

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.AddRange(
        [
            _enabledMenuItem,
            _launchAtStartupMenuItem,
            new Forms.ToolStripSeparator(),
            _versionMenuItem,
            _updateStatusMenuItem,
            _checkForUpdatesMenuItem,
            _installUpdateMenuItem,
            new Forms.ToolStripSeparator(),
            openSettingsMenuItem,
            new Forms.ToolStripSeparator(),
            exitMenuItem
        ]);

        _trayIcon = AppIconProvider.CreateTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Pop",
            Icon = _trayIcon,
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettingsWindow();
        _lastUpdateState = _updateService.CurrentState;
        _updateService.StateChanged += OnUpdateStateChanged;
        ApplyUpdateState(_lastUpdateState);
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync(_disposeCancellation.Token);
        _startupRegistration.SetLaunchAtStartup(_settings.LaunchAtStartup);
        UpdateMenuState();
        await _updateService.StartAsync(_disposeCancellation.Token);
        _dragTracker.Start();
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();

        _dragTracker.DragRejected -= OnDragRejected;
        _dragTracker.DragStarted -= OnDragStarted;
        _dragTracker.DragUpdated -= OnDragUpdated;
        _dragTracker.DragCompleted -= OnDragCompleted;
        _dragTracker.Dispose();
        _updateService.StateChanged -= OnUpdateStateChanged;
        _updateService.Dispose();
        _diagnosticsLogService.Dispose();

        if (_settingsWindow is not null)
        {
            _settingsWindow.SettingsSaved -= OnSettingsSaved;
            _settingsWindow.ClosePermanently();
            _settingsWindow = null;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
        _disposeCancellation.Dispose();
    }

    private void OnDragStarted(object? sender, DragSessionEventArgs e)
    {
        e.Session.CurrentPredictedTarget = SnapTarget.None;

        LogDiagnostics("drag-start", "Started tracking a potential throw.", new Dictionary<string, string?>
        {
            ["windowHandle"] = e.Session.WindowHandle.ToString("X"),
            ["monitorBounds"] = e.Session.MonitorInfo.WorkArea.ToString()
        });
    }

    private void OnDragUpdated(object? sender, DragSessionEventArgs e)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var decision = _snapDecider.Decide(e.Session, _settings);
        e.Session.CurrentPredictedTarget = decision.Target;
    }

    private async void OnDragCompleted(object? sender, DragSessionCompletedEventArgs e)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var decision = _snapDecider.Decide(e.Session, _settings);
        if (!decision.IsQualified)
        {
            LogDiagnostics(
                "drag-release",
                "Release did not qualify for snapping.",
                new Dictionary<string, string?>
                {
                    ["ctrlRelease"] = e.Session.IsCtrlPressedAtRelease.ToString(),
                    ["target"] = decision.Target.ToString(),
                    ["reason"] = decision.RejectionReason.ToString(),
                    ["velocityX"] = Math.Round(decision.HorizontalVelocityPxPerSec).ToString(),
                    ["velocityY"] = Math.Round(decision.VerticalVelocityPxPerSec).ToString(),
                    ["dominance"] = decision.HorizontalDominanceRatio.ToString("0.00"),
                    ["projectedLandingPoint"] = decision.ProjectedLandingPoint.ToString(),
                    ["releaseMonitor"] = e.Session.CurrentMonitorInfo.WorkArea.ToString(),
                    ["targetMonitor"] = decision.TargetMonitorInfo.WorkArea.ToString()
            });
            return;
        }

        if (e.Session.CurrentMonitorInfo != e.Session.MonitorInfo)
        {
            try
            {
                await Task.Delay(16, _disposeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        RefreshSessionState(e.Session);

        var activeMonitorInfo = decision.TargetMonitorInfo != MonitorInfo.Empty
            ? decision.TargetMonitorInfo
            : e.Session.CurrentMonitorInfo;
        var visibleTileBounds = TileLayoutCalculator.GetTileBounds(decision.Target, activeMonitorInfo);
        if (visibleTileBounds == Rectangle.Empty)
        {
            return;
        }

        var tileBounds = _snapBoundsCalculator.GetSnapBounds(e.Session.WindowHandle, visibleTileBounds);

        e.Session.CurrentPredictedTarget = decision.Target;
        var plan = _windowAnimator.CreatePlan(
            e.Session.CurrentBounds,
            tileBounds,
            decision.HorizontalVelocityPxPerSec,
            _settings.GlideDurationMs);

        LogDiagnostics(
            "drag-release",
            "Snap qualified and animation plan generated.",
            new Dictionary<string, string?>
            {
                ["ctrlRelease"] = e.Session.IsCtrlPressedAtRelease.ToString(),
                ["target"] = decision.Target.ToString(),
                ["reason"] = decision.RejectionReason.ToString(),
                ["velocityX"] = Math.Round(decision.HorizontalVelocityPxPerSec).ToString(),
                ["velocityY"] = Math.Round(decision.VerticalVelocityPxPerSec).ToString(),
                ["dominance"] = decision.HorizontalDominanceRatio.ToString("0.00"),
                ["projectedLandingPoint"] = decision.ProjectedLandingPoint.ToString(),
                ["releaseMonitor"] = e.Session.CurrentMonitorInfo.WorkArea.ToString(),
                ["targetMonitor"] = activeMonitorInfo.WorkArea.ToString(),
                ["frames"] = plan.Frames.Count.ToString(),
                ["overshootPx"] = plan.MaxOvershootPx.ToString()
            });

        try
        {
            await _windowMover.MoveWindowAsync(e.Session.WindowHandle, plan, _disposeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RefreshSessionState(DragSession session)
    {
        var state = _windowInspector.InspectWindowState(session.WindowHandle);
        if (state.Bounds != Rectangle.Empty)
        {
            session.UpdateCurrentBounds(state.Bounds);
        }

        if (state.MonitorInfo != MonitorInfo.Empty)
        {
            session.UpdateCurrentMonitorInfo(state.MonitorInfo);
        }
    }

    private void OpenSettingsWindow()
    {
        _settingsWindow ??= CreateSettingsWindow();
        _settingsWindow.ShowOrBringToFront(_settings);
    }

    private SettingsWindow CreateSettingsWindow()
    {
        var window = new SettingsWindow(_settings, _updateService);
        window.SettingsSaved += OnSettingsSaved;
        return window;
    }

    private async void OnSettingsSaved(object? sender, AppSettings settings)
    {
        await ApplySettingsAsync(settings);
    }

    private async Task ToggleEnabledAsync()
    {
        await ApplySettingsAsync(_settings with { Enabled = !_settings.Enabled });
    }

    private async Task ToggleLaunchAtStartupAsync()
    {
        await ApplySettingsAsync(_settings with { LaunchAtStartup = !_settings.LaunchAtStartup });
    }

    private async Task CheckForUpdatesAsync()
    {
        await _updateService.CheckNowAsync(_disposeCancellation.Token);
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        _settings = settings;
        _startupRegistration.SetLaunchAtStartup(settings.LaunchAtStartup);
        UpdateMenuState();
        await _settingsStore.SaveAsync(settings, _disposeCancellation.Token);
    }

    private void OnDragRejected(object? sender, DragSessionRejectedEventArgs e)
    {
        LogDiagnostics(
            "drag-ignored",
            "Pointer down did not start a Pop drag session.",
            new Dictionary<string, string?>
            {
                ["reason"] = e.InspectionResult.Eligibility.Reason.ToString(),
                ["detail"] = e.InspectionResult.Eligibility.Detail,
                ["point"] = e.ScreenPoint.ToString()
            });
    }

    private void UpdateMenuState()
    {
        _enabledMenuItem.Checked = _settings.Enabled;
        _launchAtStartupMenuItem.Checked = _settings.LaunchAtStartup;
        _notifyIcon.Text = _settings.Enabled ? "Pop - Enabled" : "Pop - Disabled";
        _versionMenuItem.Text = $"Version {AppReleaseMetadata.CurrentVersion}";
    }

    private void OnUpdateStateChanged(object? sender, UpdateStateChangedEventArgs e)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            ApplyUpdateState(e.State);
            return;
        }

        Application.Current.Dispatcher.Invoke(() => ApplyUpdateState(e.State));
    }

    private void ApplyUpdateState(UpdateState state)
    {
        var previousState = _lastUpdateState;
        _lastUpdateState = state;

        _updateStatusMenuItem.Text = GetUpdateMenuText(state);
        _checkForUpdatesMenuItem.Enabled = state.CanCheck;
        _installUpdateMenuItem.Visible = state.CanInstall;
        _installUpdateMenuItem.Enabled = state.CanInstall;
        _installUpdateMenuItem.Text = state.CanInstall && !string.IsNullOrWhiteSpace(state.AvailableVersion)
            ? $"Install Update v{state.AvailableVersion}"
            : "Install Update";

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

    private void ShowUpdateReadyNotification(UpdateState state)
    {
        _notifyIcon.BalloonTipTitle = "Pop update ready";
        _notifyIcon.BalloonTipText = string.IsNullOrWhiteSpace(state.AvailableVersion)
            ? "Restart Pop to finish installing the downloaded update."
            : $"Restart Pop to install v{state.AvailableVersion}.";
        _notifyIcon.ShowBalloonTip(5000);
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

    private void LogDiagnostics(string category, string message, IReadOnlyDictionary<string, string?>? fields = null)
    {
        if (!_settings.EnableDiagnostics)
        {
            return;
        }

        _diagnosticsLogService.Write(new DiagnosticEvent(DateTimeOffset.Now, category, message, fields));
    }
}
