using System.Drawing;
using Pop.Core.Events;
using Pop.Core.Interfaces;
using Pop.Core.Interop;
using Pop.Core.Models;

namespace Pop.Core.Services;

public sealed class MouseHookDragTracker : IDragTracker
{
    private readonly IWindowInspector _windowInspector;
    private readonly NativeMethods.LowLevelMouseProc _hookCallback;
    private IntPtr _hookHandle;
    private DragSession? _activeSession;

    public MouseHookDragTracker(IWindowInspector windowInspector)
    {
        _windowInspector = windowInspector;
        _hookCallback = HookProcedure;
    }

    public event EventHandler<DragSessionEventArgs>? DragStarted;

    public event EventHandler<DragSessionEventArgs>? DragUpdated;

    public event EventHandler<DragSessionCompletedEventArgs>? DragCompleted;

    public event EventHandler<DragSessionRejectedEventArgs>? DragRejected;

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _hookCallback, IntPtr.Zero, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to install the global mouse hook.");
        }
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _activeSession = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private IntPtr HookProcedure(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var hookData = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
                var point = hookData.Point.ToPoint();
                var timestamp = DateTimeOffset.UtcNow;
                var message = wParam.ToInt32();

                switch (message)
                {
                    case NativeMethods.WmLButtonDown:
                        HandleLeftButtonDown(point, timestamp);
                        break;
                    case NativeMethods.WmMouseMove:
                        HandleMouseMove(point, timestamp);
                        break;
                    case NativeMethods.WmLButtonUp:
                        HandleLeftButtonUp(point, timestamp);
                        break;
                }
            }
            catch
            {
                _activeSession = null;
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
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

        _activeSession.AddSample(new DragSample(point, timestamp));
        RefreshCurrentSessionState(_activeSession);
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
}
