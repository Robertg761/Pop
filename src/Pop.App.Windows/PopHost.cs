using System.Drawing;
using Pop.App.Windows.Platform.Input;
using Pop.App.Windows.Platform.Interop;
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
    private readonly QualifiedSnapPlanner _snapPlanner;
    private readonly IWindowSnapBoundsCalculator _snapBoundsCalculator;
    private readonly Win32WindowMover _windowMover;
    private readonly DiagnosticsLogService _diagnosticsLogService = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    // Guards _snapRestoreStates and _activeGlides: drag events arrive on the tracker's
    // processing thread while glide continuations complete on the thread pool.
    private readonly object _stateLock = new();
    private readonly Dictionary<IntPtr, SnapRestoreState> _snapRestoreStates = [];
    private readonly Dictionary<IntPtr, CancellationTokenSource> _activeGlides = [];
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly Forms.ToolStripMenuItem _enabledMenuItem;
    private readonly Forms.ToolStripMenuItem _launchAtStartupMenuItem;
    private readonly Forms.ToolStripMenuItem _versionMenuItem;
    private readonly Forms.ToolStripMenuItem _updateStatusMenuItem;
    private readonly Forms.ToolStripMenuItem _checkForUpdatesMenuItem;
    private readonly Forms.ToolStripMenuItem _installUpdateMenuItem;

    private AppSettings _settings = AppSettings.Default;
    private SettingsWindow? _settingsWindow;
    private UpdateState _lastUpdateState;
    private string? _lastNotifiedReadyVersion;

    public PopHost()
    {
        _settingsStore = new JsonSettingsStore();
        _startupRegistration = new WindowsStartupRegistration();
        _updateService = new UpdateService();
        _windowInspector = new WindowInspector(new WindowEligibilityEvaluator());
        _snapPlanner = new QualifiedSnapPlanner(new SnapDecider(_windowInspector.InspectMonitorAt));
        _snapBoundsCalculator = new WindowSnapBoundsCalculator();
        _windowMover = new Win32WindowMover(message => LogDiagnostics("window-move", message));

        _dragTracker = new MouseHookDragTracker(
            _windowInspector,
            diagnostics: message => LogDiagnostics("mouse-hook", message));
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
        _installUpdateMenuItem = new Forms.ToolStripMenuItem("Install Update", null, (_, _) => InstallPendingUpdate())
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
        if (!_startupRegistration.TrySetLaunchAtStartup(_settings.LaunchAtStartup))
        {
            LogDiagnostics(
                "startup",
                "Couldn't apply the launch-at-startup registration; the saved setting may not match the OS state.",
                new Dictionary<string, string?>
                {
                    ["launchAtStartup"] = _settings.LaunchAtStartup.ToString()
                });
        }

        UpdateMenuState();
        await _updateService.StartAsync(_disposeCancellation.Token);
        SyncDragTrackerWithSettings();
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

        // A re-grab must win immediately: any glide still animating this window would keep
        // fighting the user's drag with its remaining frames.
        CancelActiveGlide(e.Session.WindowHandle);

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

        TryRestorePreviousSnap(e.Session);

        var decision = _snapPlanner.Decide(e.Session, _settings);
        e.Session.CurrentPredictedTarget = decision.Target;
    }

    private async void OnDragCompleted(object? sender, DragSessionCompletedEventArgs e)
    {
        try
        {
            await HandleDragCompletedAsync(e.Session);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            LogDiagnostics(
                "drag-error",
                "Unexpected error while handling a completed drag.",
                SnapDiagnosticFields.ForUnexpectedError(e.Session.WindowHandle, exception));
        }
    }

    private async Task HandleDragCompletedAsync(DragSession session)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var decision = _snapPlanner.Decide(session, _settings);
        if (!decision.IsQualified)
        {
            LogDiagnostics(
                "drag-release",
                "Release did not qualify for snapping.",
                SnapDiagnosticFields.ForRejectedRelease(session, decision));
            return;
        }

        // Windows sometimes needs a brief settle after cross-monitor drags before bounds refresh.
        if (session.CurrentMonitorInfo != session.MonitorInfo)
        {
            await Task.Delay(16, _disposeCancellation.Token);
        }

        RefreshSessionState(session);

        if (!_snapPlanner.TryCreatePlan(
                session,
                decision,
                _settings,
                _snapBoundsCalculator.GetSnapBounds,
                out var plan))
        {
            return;
        }

        session.CurrentPredictedTarget = plan.Decision.Target;

        LogDiagnostics(
            "drag-release",
            "Snap qualified and animation plan generated.",
            SnapDiagnosticFields.ForQualifiedRelease(session, plan));

        // Record the restore state up front (not after the glide) so a re-grab mid-animation
        // can still unsnap, and register the glide so a new drag on this window can cancel it.
        var glideCancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        lock (_stateLock)
        {
            PruneDeadRestoreStatesLocked();
            _snapRestoreStates[session.WindowHandle] = new SnapRestoreState(session.InitialBounds, plan.AnimationPlan.FinalBounds);
            _activeGlides[session.WindowHandle] = glideCancellation;
        }

        try
        {
            await _windowMover.MoveWindowAsync(session.WindowHandle, plan.AnimationPlan, glideCancellation.Token);
        }
        catch (OperationCanceledException) when (!_disposeCancellation.IsCancellationRequested)
        {
            // The user re-grabbed the window mid-glide; the new drag owns it now.
        }
        finally
        {
            lock (_stateLock)
            {
                if (_activeGlides.TryGetValue(session.WindowHandle, out var current) && ReferenceEquals(current, glideCancellation))
                {
                    _activeGlides.Remove(session.WindowHandle);
                }
            }

            glideCancellation.Dispose();
        }
    }

    private void CancelActiveGlide(IntPtr windowHandle)
    {
        CancellationTokenSource? glideCancellation;
        lock (_stateLock)
        {
            _activeGlides.TryGetValue(windowHandle, out glideCancellation);
        }

        try
        {
            glideCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The glide finished and disposed its cancellation source between lookup and Cancel.
        }
    }

    private void PruneDeadRestoreStatesLocked()
    {
        List<IntPtr>? deadHandles = null;
        foreach (var handle in _snapRestoreStates.Keys)
        {
            if (!NativeMethods.IsWindow(handle))
            {
                (deadHandles ??= []).Add(handle);
            }
        }

        if (deadHandles is null)
        {
            return;
        }

        foreach (var handle in deadHandles)
        {
            _snapRestoreStates.Remove(handle);
        }
    }

    private bool TryRestorePreviousSnap(DragSession session)
    {
        SnapRestoreState restoreState;
        lock (_stateLock)
        {
            if (!_snapRestoreStates.TryGetValue(session.WindowHandle, out restoreState) || session.Samples.Count == 0)
            {
                return false;
            }

            // The restore state is consumed (or discarded) by this attempt either way.
            _snapRestoreStates.Remove(session.WindowHandle);
        }

        // HWNDs get recycled: never apply a restore rect recorded for a window that has closed.
        if (!NativeMethods.IsWindow(session.WindowHandle))
        {
            return false;
        }

        var dragSample = session.Samples[^1];
        if (!SnapRestoreCalculator.TryCreateRestoreBounds(
            session.CurrentBounds,
            restoreState.SnappedBounds,
            restoreState.RestoreBounds,
            dragSample.Position,
            session.CurrentMonitorInfo.WorkArea,
            out var restoreBounds))
        {
            return false;
        }

        // Single-frame move issued synchronously; the mover logs SetWindowPos failures itself.
        if (!_windowMover.MoveWindowImmediately(session.WindowHandle, restoreBounds))
        {
            return false;
        }

        var state = _windowInspector.InspectWindowState(session.WindowHandle);
        var actualBounds = state.Bounds != Rectangle.Empty ? state.Bounds : restoreBounds;
        session.ResetDragOrigin(actualBounds, dragSample);
        if (state.MonitorInfo != MonitorInfo.Empty)
        {
            session.UpdateCurrentMonitorInfo(state.MonitorInfo);
        }

        LogDiagnostics(
            "drag-restore",
            "Restored a previously snapped window before continuing the drag.",
            SnapDiagnosticFields.ForRestoreSuccess(session.WindowHandle, actualBounds, restoreState.SnappedBounds));

        return true;
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

    public void OpenSettingsWindow()
    {
        _settingsWindow ??= CreateSettingsWindow();
        _settingsWindow.ShowOrBringToFront(_settings);
    }

    private SettingsWindow CreateSettingsWindow()
    {
        return new SettingsWindow(_settings, _updateService, ApplySettingsAsync);
    }

    private async Task ToggleEnabledAsync()
    {
        await ApplySettingsAsync(_settings with { Enabled = !_settings.Enabled });
    }

    private async Task ToggleLaunchAtStartupAsync()
    {
        await ApplySettingsAsync(_settings with { LaunchAtStartup = !_settings.LaunchAtStartup });
    }

    private void InstallPendingUpdate()
    {
        try
        {
            _updateService.ApplyPendingUpdateAndRestart();
        }
        catch (Exception exception)
        {
            LogDiagnostics(
                "update-install",
                "Failed to apply the pending update from the tray menu.",
                new Dictionary<string, string?>
                {
                    ["error"] = exception.GetType().Name,
                    ["message"] = exception.Message
                });

            System.Windows.MessageBox.Show(
                $"Pop couldn't install the update.\n\n{exception.Message}",
                "Pop Update Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await _updateService.CheckNowAsync(_disposeCancellation.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            // The service was cancelled/disposed while a manual check ran (e.g. app shutting down).
        }
    }

    private async Task<bool> ApplySettingsAsync(AppSettings settings)
    {
        var previousSettings = _settings;
        var launchAtStartupChanged = settings.LaunchAtStartup != previousSettings.LaunchAtStartup;

        try
        {
            if (launchAtStartupChanged && !_startupRegistration.TrySetLaunchAtStartup(settings.LaunchAtStartup))
            {
                throw new InvalidOperationException("Couldn't update the launch-at-startup registration.");
            }

            await _settingsStore.SaveAsync(settings, _disposeCancellation.Token);

            _settings = settings;
            UpdateMenuState();
            SyncDragTrackerWithSettings();
            return true;
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            if (launchAtStartupChanged)
            {
                TryRestoreLaunchAtStartup(previousSettings.LaunchAtStartup);
            }

            _settings = previousSettings;
            UpdateMenuState();
            try
            {
                SyncDragTrackerWithSettings();
            }
            catch
            {
                // Re-syncing the hook after a failed save is best-effort; the error dialog below
                // already tells the user something went wrong.
            }

            ShowSettingsSaveError(exception);
            return false;
        }
    }

    // The global mouse hook (and the per-click window inspection it triggers) should only run
    // while Pop is enabled; Start/Stop are idempotent.
    private void SyncDragTrackerWithSettings()
    {
        if (_settings.Enabled)
        {
            _dragTracker.Start();
        }
        else
        {
            _dragTracker.Stop();
        }
    }

    private void TryRestoreLaunchAtStartup(bool enabled)
    {
        _startupRegistration.TrySetLaunchAtStartup(enabled);
    }

    private static void ShowSettingsSaveError(Exception exception)
    {
        System.Windows.MessageBox.Show(
            $"Pop couldn't save settings.\n\n{exception.Message}",
            "Pop Settings Error",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
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
        // The background update loop can raise this after Run() returns, when Application.Current
        // is already null or the dispatcher is shutting down.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ApplyUpdateState(e.State);
            return;
        }

        try
        {
            dispatcher.Invoke(() => ApplyUpdateState(e.State));
        }
        catch (Exception exception) when (exception is System.Threading.Tasks.TaskCanceledException or OperationCanceledException)
        {
            // Dispatcher is shutting down; drop the stale update-state notification.
        }
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
