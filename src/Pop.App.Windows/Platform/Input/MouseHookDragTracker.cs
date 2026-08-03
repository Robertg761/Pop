using System.Drawing;
using System.Threading.Channels;
using System.Windows.Threading;
using Pop.App.Windows.Platform.Interop;
using Pop.Core.Events;
using Pop.Core.Models;
using Pop.Platform.Abstractions.Input;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Windows.Platform.Input;

public sealed class MouseHookDragTracker : IDragTracker
{
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly IWindowInspector _windowInspector;
    private readonly Func<bool> _isCtrlPressedAccessor;
    private readonly Action<string>? _diagnostics;
    private readonly NativeMethods.LowLevelMouseProc _hookCallback;
    private IntPtr _hookHandle;
    private DragSession? _activeSession;
    private bool _started;
    private bool _hookSawLeftButtonDown;
    private long _hookCallbackCount;
    private long _lastObservedCallbackCount;
    private Point? _lastObservedCursorPosition;
    private Channel<MouseHookEvent>? _eventChannel;
    private Task? _eventProcessingTask;
    private DispatcherTimer? _healthTimer;

    public MouseHookDragTracker(
        IWindowInspector windowInspector,
        Func<bool>? isCtrlPressedAccessor = null,
        Action<string>? diagnostics = null)
    {
        _windowInspector = windowInspector;
        _isCtrlPressedAccessor = isCtrlPressedAccessor ?? IsCtrlPressed;
        _diagnostics = diagnostics;
        _hookCallback = HookProcedure;
    }

    public event EventHandler<DragSessionEventArgs>? DragStarted;

    public event EventHandler<DragSessionEventArgs>? DragUpdated;

    public event EventHandler<DragSessionCompletedEventArgs>? DragCompleted;

    public event EventHandler<DragSessionRejectedEventArgs>? DragRejected;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        var eventChannel = Channel.CreateUnbounded<MouseHookEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        _eventChannel = eventChannel;
        _hookSawLeftButtonDown = false;

        try
        {
            InstallHook();
        }
        catch
        {
            eventChannel.Writer.TryComplete();
            _eventChannel = null;
            throw;
        }

        _eventProcessingTask = Task.Run(() => ProcessEventsAsync(eventChannel.Reader));
        _hookCallbackCount = 0;
        _lastObservedCallbackCount = 0;
        _lastObservedCursorPosition = null;

        // Windows silently removes a low-level hook whose callback stalls; watch for mouse
        // movement without hook callbacks and reinstall the hook if that ever happens.
        _healthTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = HealthCheckInterval
        };
        _healthTimer.Tick += OnHealthTimerTick;
        _healthTimer.Start();
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;

        if (_healthTimer is not null)
        {
            _healthTimer.Stop();
            _healthTimer.Tick -= OnHealthTimerTick;
            _healthTimer = null;
        }

        RemoveHook();
        _hookSawLeftButtonDown = false;

        _eventChannel?.Writer.TryComplete();
        _eventChannel = null;

        // Drain the queued events so no drag events fire after Stop() returns; the processing
        // loop clears the active session when it finishes.
        try
        {
            _eventProcessingTask?.Wait(StopDrainTimeout);
        }
        catch (AggregateException)
        {
        }

        _eventProcessingTask = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void InstallHook()
    {
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _hookCallback, IntPtr.Zero, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Unable to install the global mouse hook (Win32 error {error}: {new System.ComponentModel.Win32Exception(error).Message}).");
        }
    }

    private void RemoveHook()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.UnhookWindowsHookEx(_hookHandle))
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            _diagnostics?.Invoke($"UnhookWindowsHookEx failed (Win32 error {error}).");
        }

        _hookHandle = IntPtr.Zero;
    }

    private void OnHealthTimerTick(object? sender, EventArgs e)
    {
        if (!_started)
        {
            return;
        }

        var hookFired = _hookCallbackCount != _lastObservedCallbackCount;
        _lastObservedCallbackCount = _hookCallbackCount;

        var hasCursor = NativeMethods.GetCursorPos(out var cursor);
        var cursorMoved = hasCursor &&
                          _lastObservedCursorPosition is { } previousPosition &&
                          (previousPosition.X != cursor.X || previousPosition.Y != cursor.Y);
        if (hasCursor)
        {
            _lastObservedCursorPosition = cursor.ToPoint();
        }

        if (_hookHandle != IntPtr.Zero && (hookFired || !cursorMoved))
        {
            return;
        }

        // The mouse moved but our hook never fired (or a previous reinstall failed): Windows
        // has dropped the hook, so reinstall it.
        RemoveHook();
        _hookSawLeftButtonDown = false;

        try
        {
            InstallHook();
            _diagnostics?.Invoke("Reinstalled the low-level mouse hook after it stopped receiving events.");
        }
        catch (InvalidOperationException exception)
        {
            // Leave _hookHandle zero; the next tick retries.
            _diagnostics?.Invoke(exception.Message);
        }
    }

    private IntPtr HookProcedure(int nCode, IntPtr wParam, IntPtr lParam)
    {
        _hookCallbackCount++;

        if (nCode >= 0)
        {
            // Keep this callback near-instant: Windows silently unhooks callbacks that stall.
            // Capture the event data and defer all inspection/planning to the processing task.
            try
            {
                var message = wParam.ToInt32();
                var isButtonEvent = message is NativeMethods.WmLButtonDown or NativeMethods.WmLButtonUp;
                if (isButtonEvent || (message == NativeMethods.WmMouseMove && _hookSawLeftButtonDown))
                {
                    if (message == NativeMethods.WmLButtonDown)
                    {
                        _hookSawLeftButtonDown = true;
                    }
                    else if (message == NativeMethods.WmLButtonUp)
                    {
                        _hookSawLeftButtonDown = false;
                    }

                    var hookData = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
                    _eventChannel?.Writer.TryWrite(new MouseHookEvent(message, hookData.Point.ToPoint(), DateTimeOffset.UtcNow));
                }
            }
            catch
            {
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private async Task ProcessEventsAsync(ChannelReader<MouseHookEvent> reader)
    {
        try
        {
            await foreach (var hookEvent in reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    switch (hookEvent.Message)
                    {
                        case NativeMethods.WmLButtonDown:
                            HandleLeftButtonDown(hookEvent.Point, hookEvent.Timestamp);
                            break;
                        case NativeMethods.WmMouseMove:
                            HandleMouseMove(hookEvent.Point, hookEvent.Timestamp);
                            break;
                        case NativeMethods.WmLButtonUp:
                            HandleLeftButtonUp(hookEvent.Point, hookEvent.Timestamp);
                            break;
                    }
                }
                catch
                {
                    _activeSession = null;
                }
            }
        }
        finally
        {
            _activeSession = null;
        }
    }

    private void HandleLeftButtonDown(System.Drawing.Point point, DateTimeOffset timestamp)
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

    private void HandleMouseMove(System.Drawing.Point point, DateTimeOffset timestamp)
    {
        if (_activeSession is null)
        {
            return;
        }

        _activeSession.AddSample(new DragSample(point, timestamp));
        RefreshCurrentSessionState(_activeSession);
        DragUpdated?.Invoke(this, new DragSessionEventArgs(_activeSession));
    }

    private void HandleLeftButtonUp(System.Drawing.Point point, DateTimeOffset timestamp)
    {
        if (_activeSession is null)
        {
            return;
        }

        var releaseSample = new DragSample(point, timestamp);
        _activeSession.AddSample(releaseSample);
        RefreshCurrentSessionState(_activeSession);
        _activeSession.CompleteRelease(releaseSample, _isCtrlPressedAccessor());
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

    private static bool IsCtrlPressed()
    {
        return (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
    }

    private readonly record struct MouseHookEvent(int Message, Point Point, DateTimeOffset Timestamp);
}
