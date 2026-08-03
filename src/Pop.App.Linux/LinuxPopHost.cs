using System.Drawing;
using Pop.App.Linux.Platform;
using Pop.App.Linux.Platform.KWin;
using Pop.App.Linux.Platform.Startup;
using Pop.App.Linux.Platform.X11;
using Pop.App.Linux.Services;
using Pop.Core.Events;
using Pop.Core.Interfaces;
using Pop.Core.Models;
using Pop.Core.Services;
using Pop.Platform.Abstractions.Input;
using Pop.Platform.Abstractions.Startup;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Linux;

public sealed class LinuxPopHost : IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly IStartupRegistration _startupRegistration = new LinuxStartupRegistration();
    private readonly KWinWaylandIntegration? _kwinWaylandIntegration;
    private readonly X11DisplayConnection? _displayConnection;
    private readonly IDragTracker? _dragTracker;
    private readonly IWindowInspector? _windowInspector;
    private readonly QualifiedSnapPlanner? _snapPlanner;
    private readonly IWindowSnapBoundsCalculator? _snapBoundsCalculator;
    private readonly IWindowMover? _windowMover;
    private readonly WindowAnimator _windowAnimator = new(60d);
    private readonly DiagnosticsLogService _diagnosticsLogService = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Dictionary<IntPtr, SnapRestoreState> _snapRestoreStates = [];
    private readonly object _snapRestoreLock = new();

    private AppSettings _settings = AppSettings.Default;

    public AppSettings Settings => _settings;

    /// <summary>
    /// Non-null when this desktop session cannot support snapping (e.g. GNOME or Sway Wayland).
    /// The app still runs so the tray/settings stay reachable, but no tracker is started.
    /// </summary>
    public string? UnsupportedSessionMessage { get; }

    public LinuxPopHost()
    {
        _settingsStore = new JsonSettingsStore(LinuxPaths.ConfigDirectory);

        if (KWinWaylandIntegration.IsCandidateSession())
        {
            _kwinWaylandIntegration = new KWinWaylandIntegration();
            return;
        }

        if (LinuxSessionEnvironment.IsWaylandSession())
        {
            // A non-KDE Wayland session: the X11 path below would only ever see XWayland
            // windows, so snapping would silently no-op for native windows. Surface a clear
            // message instead of pretending to work.
            UnsupportedSessionMessage =
                "Pop currently supports X11 and KDE Plasma (Wayland) sessions only, so window snapping " +
                "is disabled in this Wayland session. Set POP_FORCE_X11=1 to snap XWayland windows anyway.";
            return;
        }

        _displayConnection = X11DisplayConnection.Open();
        _windowInspector = new X11WindowInspector(_displayConnection, new WindowEligibilityEvaluator());
        _snapPlanner = new QualifiedSnapPlanner(
            new SnapDecider(_windowInspector.InspectMonitorAt),
            _windowAnimator);
        _snapBoundsCalculator = new X11WindowSnapBoundsCalculator();
        _windowMover = new X11WindowMover(_displayConnection);
        _dragTracker = new X11PollingDragTracker(
            _displayConnection,
            _windowInspector,
            isTrackingEnabledAccessor: () => _settings.Enabled);

        _dragTracker.DragRejected += OnDragRejected;
        _dragTracker.DragStarted += OnDragStarted;
        _dragTracker.DragUpdated += OnDragUpdated;
        _dragTracker.DragCompleted += OnDragCompleted;
    }

    public async Task InitializeAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Pop.App.Linux can only run on Linux.");
        }

        _settings = await _settingsStore.LoadAsync(_disposeCancellation.Token);
        ApplyStartupRegistration();

        if (_kwinWaylandIntegration is not null)
        {
            await _kwinWaylandIntegration.InitializeAsync(_settings, _disposeCancellation.Token);
            Console.WriteLine("Pop installed its KWin Wayland integration for Plasma.");
            return;
        }

        if (UnsupportedSessionMessage is not null)
        {
            return;
        }

        _dragTracker!.Start();
    }

    private void ApplyStartupRegistration()
    {
        // Best effort: keep the autostart entry in sync with the persisted flag. When enabled,
        // rewrite it so the Exec path follows a moved AppImage; when disabled, only delete an
        // entry that actually exists.
        if (_settings.LaunchAtStartup)
        {
            if (!_startupRegistration.TrySetLaunchAtStartup(true))
            {
                Console.Error.WriteLine("Pop couldn't refresh its launch-at-startup entry.");
            }
        }
        else if (_startupRegistration.IsLaunchAtStartupEnabled() == true)
        {
            _startupRegistration.TrySetLaunchAtStartup(false);
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        // Register with the OS before persisting so a failure leaves settings and reality in
        // sync; revert the registration if the persist itself then fails.
        var launchAtStartupChanged = settings.LaunchAtStartup != _settings.LaunchAtStartup;
        if (launchAtStartupChanged && !_startupRegistration.TrySetLaunchAtStartup(settings.LaunchAtStartup))
        {
            throw new InvalidOperationException("Pop couldn't update the launch-at-startup entry in ~/.config/autostart.");
        }

        try
        {
            // Persist first, then adopt in memory, so a failed write leaves memory and disk in sync.
            await _settingsStore.SaveAsync(settings, _disposeCancellation.Token);
        }
        catch
        {
            if (launchAtStartupChanged)
            {
                _startupRegistration.TrySetLaunchAtStartup(_settings.LaunchAtStartup);
            }

            throw;
        }

        _settings = settings;

        if (_kwinWaylandIntegration is not null)
        {
            await _kwinWaylandIntegration.ReloadAsync(settings, _disposeCancellation.Token);
        }
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();
        if (_dragTracker is not null)
        {
            _dragTracker.DragRejected -= OnDragRejected;
            _dragTracker.DragStarted -= OnDragStarted;
            _dragTracker.DragUpdated -= OnDragUpdated;
            _dragTracker.DragCompleted -= OnDragCompleted;
            _dragTracker.Dispose();
        }

        _kwinWaylandIntegration?.Dispose();
        _diagnosticsLogService.Dispose();
        if (_displayConnection is not null)
        {
            // Never free the Display while a stalled poll task might still be using it: leaking
            // the connection at process exit is safer than a native use-after-free.
            if (_dragTracker is X11PollingDragTracker { HasStalledPollTask: true })
            {
                Console.Error.WriteLine("Pop is leaking its X11 display connection because the drag poller did not stop in time.");
            }
            else
            {
                _displayConnection.Dispose();
            }
        }

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

        TryRestorePreviousSnap(e.Session);

        var decision = _snapPlanner!.Decide(e.Session, _settings);
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
            Console.Error.WriteLine($"Pop drag error: {exception.Message}");
        }
    }

    private async Task HandleDragCompletedAsync(DragSession session)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var decision = _snapPlanner!.Decide(session, _settings);
        if (!decision.IsQualified)
        {
            LogDiagnostics(
                "drag-release",
                "Release did not qualify for snapping.",
                SnapDiagnosticFields.ForRejectedRelease(session, decision));
            return;
        }

        RefreshSessionState(session);

        if (!_snapPlanner.TryCreatePlan(
                session,
                decision,
                _settings,
                _snapBoundsCalculator!.GetSnapBounds,
                out var plan))
        {
            return;
        }

        LogDiagnostics(
            "drag-release",
            "Snap qualified and animation plan generated.",
            SnapDiagnosticFields.ForQualifiedRelease(session, plan));

        await _windowMover!.MoveWindowAsync(session.WindowHandle, plan.AnimationPlan, _disposeCancellation.Token);
        lock (_snapRestoreLock)
        {
            _snapRestoreStates[session.WindowHandle] = new SnapRestoreState(session.InitialBounds, plan.AnimationPlan.FinalBounds);
        }
    }

    private bool TryRestorePreviousSnap(DragSession session)
    {
        SnapRestoreState restoreState;
        lock (_snapRestoreLock)
        {
            if (!_snapRestoreStates.TryGetValue(session.WindowHandle, out restoreState) || session.Samples.Count == 0)
            {
                return false;
            }
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
            lock (_snapRestoreLock)
            {
                _snapRestoreStates.Remove(session.WindowHandle);
            }
            return false;
        }

        lock (_snapRestoreLock)
        {
            _snapRestoreStates.Remove(session.WindowHandle);
        }
        try
        {
            _windowMover!.MoveWindowAsync(
                session.WindowHandle,
                WindowAnimator.CreateImmediatePlan(restoreBounds),
                _disposeCancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LogDiagnostics(
                "drag-restore",
                "Failed to restore a previously snapped window.",
                SnapDiagnosticFields.ForRestoreFailure(session.WindowHandle, exception));
            return false;
        }

        var state = _windowInspector!.InspectWindowState(session.WindowHandle);
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
        var state = _windowInspector!.InspectWindowState(session.WindowHandle);
        if (state.Bounds != Rectangle.Empty)
        {
            session.UpdateCurrentBounds(state.Bounds);
        }

        if (state.MonitorInfo != MonitorInfo.Empty)
        {
            session.UpdateCurrentMonitorInfo(state.MonitorInfo);
        }
    }

    private void OnDragRejected(object? sender, DragSessionRejectedEventArgs e)
    {
        LogDiagnostics("drag-ignored", "Pointer down did not start a Pop drag session.", new Dictionary<string, string?>
        {
            ["reason"] = e.InspectionResult.Eligibility.Reason.ToString(),
            ["detail"] = e.InspectionResult.Eligibility.Detail,
            ["point"] = e.ScreenPoint.ToString()
        });
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
