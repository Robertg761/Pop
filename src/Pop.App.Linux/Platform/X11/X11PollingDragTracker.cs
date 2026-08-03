using System.Drawing;
using Pop.Core.Events;
using Pop.Core.Models;
using Pop.Platform.Abstractions.Input;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Linux.Platform.X11;

public sealed class X11PollingDragTracker : IDragTracker
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(8);
    // When snapping is disabled we only need to notice the setting flipping back on, so an idle
    // disabled Pop polls two orders of magnitude slower and skips all window inspection.
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StateRefreshInterval = TimeSpan.FromMilliseconds(64);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private readonly X11DisplayConnection _connection;
    private readonly IWindowInspector _windowInspector;
    private readonly Func<uint, bool> _isCtrlPressedAccessor;
    private readonly Func<bool> _isTrackingEnabledAccessor;
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollingTask;
    private Task? _stalledPollTask;
    private bool _disposed;
    private DragSession? _activeSession;
    private bool _wasLeftButtonDown;
    private DateTimeOffset _nextStateRefreshAt;

    public X11PollingDragTracker(
        X11DisplayConnection connection,
        IWindowInspector windowInspector,
        Func<uint, bool>? isCtrlPressedAccessor = null,
        Func<bool>? isTrackingEnabledAccessor = null)
    {
        _connection = connection;
        _windowInspector = windowInspector;
        _isCtrlPressedAccessor = isCtrlPressedAccessor ?? IsCtrlPressed;
        _isTrackingEnabledAccessor = isTrackingEnabledAccessor ?? (static () => true);
    }

    public event EventHandler<DragSessionRejectedEventArgs>? DragRejected;

    public event EventHandler<DragSessionEventArgs>? DragStarted;

    public event EventHandler<DragSessionEventArgs>? DragUpdated;

    public event EventHandler<DragSessionCompletedEventArgs>? DragCompleted;

    public void Start()
    {
        // The cancellation source is created per Start so Stop/Start cycles work; the previous
        // design cancelled a shared source that could never be rearmed.
        if (_disposed || _pollingTask is not null || HasStalledPollTask)
        {
            return;
        }

        var pollCancellation = new CancellationTokenSource();
        _pollCancellation = pollCancellation;
        _pollingTask = Task.Run(() => PollAsync(pollCancellation.Token));
    }

    public void Stop()
    {
        var pollCancellation = _pollCancellation;
        var pollingTask = _pollingTask;
        _pollCancellation = null;
        _pollingTask = null;
        pollCancellation?.Cancel();

        if (pollingTask is not null)
        {
            try
            {
                if (!pollingTask.Wait(StopTimeout))
                {
                    // Track the runaway task so the host knows it must not free the X11 Display
                    // underneath it; the cancellation source is intentionally leaked with it.
                    _stalledPollTask = pollingTask;
                }
            }
            catch (AggregateException)
            {
            }
        }

        if (_stalledPollTask is null)
        {
            pollCancellation?.Dispose();
        }

        _activeSession = null;
        _wasLeftButtonDown = false;
        _nextStateRefreshAt = DateTimeOffset.MinValue;
    }

    // True while a poll task failed to stop within the timeout and may still touch the display.
    public bool HasStalledPollTask => _stalledPollTask is { IsCompleted: false };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_isTrackingEnabledAccessor())
            {
                _activeSession = null;
                _wasLeftButtonDown = false;
                await Task.Delay(DisabledPollInterval, cancellationToken);
                continue;
            }

            // Fail soft on the hot path: an exception from a window inspection or a handler must
            // not fault the polling task, which would silently stop all snapping for the session.
            try
            {
                var snapshot = QueryPointer();
                var isLeftButtonDown = (snapshot.ButtonMask & X11Native.Button1Mask) != 0;
                var timestamp = DateTimeOffset.UtcNow;

                if (isLeftButtonDown && !_wasLeftButtonDown)
                {
                    HandleLeftButtonDown(snapshot.Position, timestamp);
                }
                else if (isLeftButtonDown)
                {
                    HandleMouseMove(snapshot.Position, timestamp);
                }
                else if (_wasLeftButtonDown)
                {
                    HandleLeftButtonUp(snapshot.Position, timestamp, snapshot.ButtonMask);
                }

                _wasLeftButtonDown = isLeftButtonDown;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _activeSession = null;
                _wasLeftButtonDown = false;
                Console.Error.WriteLine($"Pop drag polling recovered from an error: {exception.Message}");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private X11PointerSnapshot QueryPointer()
    {
        int success;
        int rootX;
        int rootY;
        uint mask;
        lock (_connection.SyncRoot)
        {
            if (_connection.IsDisposed)
            {
                return X11PointerSnapshot.Empty;
            }

            success = X11Native.XQueryPointer(
                _connection.Display,
                _connection.RootWindow,
                out _,
                out _,
                out rootX,
                out rootY,
                out _,
                out _,
                out mask);
        }

        return success == X11Native.False
            ? X11PointerSnapshot.Empty
            : new X11PointerSnapshot(new Point(rootX, rootY), mask);
    }

    private void HandleLeftButtonDown(Point point, DateTimeOffset timestamp)
    {
        _activeSession = null;

        var inspection = _windowInspector.InspectWindowAt(point);
        if (!inspection.Eligibility.IsSupported || inspection.WindowHandle == IntPtr.Zero)
        {
            DragRejected?.Invoke(this, new DragSessionRejectedEventArgs(point, inspection));
            return;
        }

        var session = new DragSession(inspection.WindowHandle, inspection.MonitorInfo, inspection.Bounds);
        session.AddSample(new DragSample(point, timestamp));
        RefreshCurrentSessionState(session);
        _nextStateRefreshAt = timestamp + StateRefreshInterval;
        _activeSession = session;
        DragStarted?.Invoke(this, new DragSessionEventArgs(session));
    }

    private void HandleMouseMove(Point point, DateTimeOffset timestamp)
    {
        if (_activeSession is null)
        {
            return;
        }

        _activeSession.AddSample(new DragSample(point, timestamp));
        _activeSession.UpdateCurrentBounds(_activeSession.GetCurrentBoundsEstimate());
        if (timestamp >= _nextStateRefreshAt)
        {
            RefreshCurrentSessionState(_activeSession);
            _nextStateRefreshAt = timestamp + StateRefreshInterval;
        }

        DragUpdated?.Invoke(this, new DragSessionEventArgs(_activeSession));
    }

    private void HandleLeftButtonUp(Point point, DateTimeOffset timestamp, uint buttonMask)
    {
        if (_activeSession is null)
        {
            return;
        }

        var releaseSample = new DragSample(point, timestamp);
        _activeSession.AddSample(releaseSample);
        RefreshCurrentSessionState(_activeSession);
        _activeSession.CompleteRelease(releaseSample, _isCtrlPressedAccessor(buttonMask));
        DragCompleted?.Invoke(this, new DragSessionCompletedEventArgs(_activeSession));
        _activeSession = null;
    }

    private void RefreshCurrentSessionState(DragSession session)
    {
        var windowState = _windowInspector.InspectWindowState(session.WindowHandle);
        if (windowState.Bounds != Rectangle.Empty)
        {
            session.UpdateCurrentBounds(windowState.Bounds);
        }

        if (windowState.MonitorInfo != MonitorInfo.Empty)
        {
            session.UpdateCurrentMonitorInfo(windowState.MonitorInfo);
        }
    }

    private static bool IsCtrlPressed(uint buttonMask)
    {
        return (buttonMask & X11Native.ControlMask) != 0;
    }

    private readonly record struct X11PointerSnapshot(Point Position, uint ButtonMask)
    {
        public static X11PointerSnapshot Empty { get; } = new(Point.Empty, 0);
    }
}
