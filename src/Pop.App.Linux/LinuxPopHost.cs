using System.Drawing;
using Pop.App.Linux.Platform.KWin;
using Pop.App.Linux.Platform.X11;
using Pop.App.Linux.Services;
using Pop.Core.Events;
using Pop.Core.Interfaces;
using Pop.Core.Models;
using Pop.Core.Services;
using Pop.Platform.Abstractions.Input;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Linux;

public sealed class LinuxPopHost : IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly KWinWaylandIntegration? _kwinWaylandIntegration;
    private readonly X11DisplayConnection? _displayConnection;
    private readonly IDragTracker? _dragTracker;
    private readonly IWindowInspector? _windowInspector;
    private readonly ISnapDecider? _snapDecider;
    private readonly IWindowSnapBoundsCalculator? _snapBoundsCalculator;
    private readonly IWindowMover? _windowMover;
    private readonly WindowAnimator _windowAnimator = new(60d);
    private readonly DiagnosticsLogService _diagnosticsLogService = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Dictionary<IntPtr, SnapRestoreState> _snapRestoreStates = [];

    private AppSettings _settings = new();

    public AppSettings Settings => _settings;

    public LinuxPopHost()
    {
        _settingsStore = new JsonSettingsStore(LinuxPaths.ConfigDirectory);

        if (KWinWaylandIntegration.IsCandidateSession())
        {
            _kwinWaylandIntegration = new KWinWaylandIntegration();
            return;
        }

        _displayConnection = X11DisplayConnection.Open();
        _windowInspector = new X11WindowInspector(_displayConnection, new WindowEligibilityEvaluator());
        _snapDecider = new SnapDecider(_windowInspector.InspectMonitorAt);
        _snapBoundsCalculator = new X11WindowSnapBoundsCalculator();
        _windowMover = new X11WindowMover(_displayConnection);
        _dragTracker = new X11PollingDragTracker(_displayConnection, _windowInspector);

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

        if (_kwinWaylandIntegration is not null)
        {
            await _kwinWaylandIntegration.InitializeAsync(_settings, _disposeCancellation.Token);
            Console.WriteLine("Pop installed its KWin Wayland integration for Plasma.");
            return;
        }

        _dragTracker!.Start();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        _settings = settings;
        await _settingsStore.SaveAsync(settings, _disposeCancellation.Token);

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
        _displayConnection?.Dispose();
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

        var decision = _snapDecider!.Decide(e.Session, _settings);
        e.Session.CurrentPredictedTarget = decision.Target;
    }

    private async void OnDragCompleted(object? sender, DragSessionCompletedEventArgs e)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var decision = _snapDecider!.Decide(e.Session, _settings);
        if (!decision.IsQualified)
        {
            LogDiagnostics("drag-release", "Release did not qualify for snapping.", new Dictionary<string, string?>
            {
                ["ctrlRelease"] = e.Session.IsCtrlPressedAtRelease.ToString(),
                ["target"] = decision.Target.ToString(),
                ["reason"] = decision.RejectionReason.ToString(),
                ["velocityX"] = Math.Round(decision.HorizontalVelocityPxPerSec).ToString(),
                ["velocityY"] = Math.Round(decision.VerticalVelocityPxPerSec).ToString()
            });
            return;
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

        var tileBounds = _snapBoundsCalculator!.GetSnapBounds(e.Session.WindowHandle, visibleTileBounds);
        var plan = _windowAnimator.CreatePlan(
            e.Session.CurrentBounds,
            tileBounds,
            decision.HorizontalVelocityPxPerSec,
            _settings.GlideDurationMs);

        LogDiagnostics("drag-release", "Snap qualified and animation plan generated.", new Dictionary<string, string?>
        {
            ["target"] = decision.Target.ToString(),
            ["targetMonitor"] = activeMonitorInfo.WorkArea.ToString(),
            ["frames"] = plan.Frames.Count.ToString()
        });

        try
        {
            await _windowMover!.MoveWindowAsync(e.Session.WindowHandle, plan, _disposeCancellation.Token);
            _snapRestoreStates[e.Session.WindowHandle] = new SnapRestoreState(e.Session.InitialBounds, plan.FinalBounds);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool TryRestorePreviousSnap(DragSession session)
    {
        if (!_snapRestoreStates.TryGetValue(session.WindowHandle, out var restoreState) || session.Samples.Count == 0)
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
            _snapRestoreStates.Remove(session.WindowHandle);
            return false;
        }

        _snapRestoreStates.Remove(session.WindowHandle);
        try
        {
            _windowMover!.MoveWindowAsync(
                session.WindowHandle,
                CreateImmediateMovePlan(restoreBounds),
                _disposeCancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LogDiagnostics("drag-restore", "Failed to restore a previously snapped window.", new Dictionary<string, string?>
            {
                ["windowHandle"] = session.WindowHandle.ToString("X"),
                ["error"] = exception.Message
            });
            return false;
        }

        var state = _windowInspector!.InspectWindowState(session.WindowHandle);
        var actualBounds = state.Bounds != Rectangle.Empty ? state.Bounds : restoreBounds;
        session.ResetDragOrigin(actualBounds, dragSample);
        if (state.MonitorInfo != MonitorInfo.Empty)
        {
            session.UpdateCurrentMonitorInfo(state.MonitorInfo);
        }

        LogDiagnostics("drag-restore", "Restored a previously snapped window before continuing the drag.", new Dictionary<string, string?>
        {
            ["windowHandle"] = session.WindowHandle.ToString("X"),
            ["restoreBounds"] = actualBounds.ToString(),
            ["snappedBounds"] = restoreState.SnappedBounds.ToString()
        });

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

    private static AnimationPlan CreateImmediateMovePlan(Rectangle bounds)
    {
        return new AnimationPlan(Array.Empty<AnimationFrame>(), bounds, 0, 0);
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
