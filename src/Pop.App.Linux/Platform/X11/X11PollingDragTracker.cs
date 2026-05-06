using System.Drawing;
using Pop.Core.Events;
using Pop.Core.Models;
using Pop.Platform.Abstractions.Input;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Linux.Platform.X11;

public sealed class X11PollingDragTracker : IDragTracker
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(8);
    private readonly X11DisplayConnection _connection;
    private readonly IWindowInspector _windowInspector;
    private readonly Func<uint, bool> _isCtrlPressedAccessor;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private Task? _pollingTask;
    private DragSession? _activeSession;
    private bool _wasLeftButtonDown;

    public X11PollingDragTracker(
        X11DisplayConnection connection,
        IWindowInspector windowInspector,
        Func<uint, bool>? isCtrlPressedAccessor = null)
    {
        _connection = connection;
        _windowInspector = windowInspector;
        _isCtrlPressedAccessor = isCtrlPressedAccessor ?? IsCtrlPressed;
    }

    public event EventHandler<DragSessionRejectedEventArgs>? DragRejected;

    public event EventHandler<DragSessionEventArgs>? DragStarted;

    public event EventHandler<DragSessionEventArgs>? DragUpdated;

    public event EventHandler<DragSessionCompletedEventArgs>? DragCompleted;

    public void Start()
    {
        if (_pollingTask is not null)
        {
            return;
        }

        _pollingTask = Task.Run(() => PollAsync(_disposeCancellation.Token));
    }

    public void Stop()
    {
        _disposeCancellation.Cancel();
        try
        {
            _pollingTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _pollingTask = null;
        _activeSession = null;
        _wasLeftButtonDown = false;
    }

    public void Dispose()
    {
        Stop();
        _disposeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
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
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private X11PointerSnapshot QueryPointer()
    {
        var success = X11Native.XQueryPointer(
            _connection.Display,
            _connection.RootWindow,
            out _,
            out _,
            out var rootX,
            out var rootY,
            out _,
            out _,
            out var mask);

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
        RefreshCurrentSessionState(_activeSession);
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
