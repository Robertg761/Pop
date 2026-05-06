using System.Drawing;
using Pop.Core.Models;
using Pop.Core.Services;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Linux.Platform.X11;

public sealed class X11WindowInspector : IWindowInspector
{
    private const int FallbackTitleBarHeight = 48;
    private readonly X11DisplayConnection _connection;
    private readonly WindowEligibilityEvaluator _evaluator;

    public X11WindowInspector(X11DisplayConnection connection, WindowEligibilityEvaluator evaluator)
    {
        _connection = connection;
        _evaluator = evaluator;
    }

    public WindowInspectionResult InspectWindowAt(Point screenPoint)
    {
        var pointer = QueryPointer();
        if (pointer.ChildWindow == IntPtr.Zero)
        {
            return CreateUnsupportedResult();
        }

        var frameWindow = pointer.ChildWindow;
        var clientWindow = FindClientWindow(frameWindow);
        if (clientWindow == IntPtr.Zero)
        {
            return CreateUnsupportedResult();
        }

        var bounds = GetWindowBounds(clientWindow);
        var frameBounds = GetWindowBounds(frameWindow);
        var monitorInfo = InspectMonitorAt(screenPoint);
        var traits = BuildTraits(clientWindow, frameWindow, screenPoint, bounds, frameBounds, monitorInfo);
        var eligibility = _evaluator.Evaluate(traits);

        return new WindowInspectionResult(clientWindow, bounds, monitorInfo, traits, eligibility);
    }

    public MonitorInfo InspectMonitorAt(Point screenPoint)
    {
        _ = screenPoint;
        var displayBounds = new Rectangle(
            0,
            0,
            X11Native.XDisplayWidth(_connection.Display, _connection.Screen),
            X11Native.XDisplayHeight(_connection.Display, _connection.Screen));

        var workAreaValues = X11PropertyReader.ReadLongArray(_connection, _connection.RootWindow, _connection.Atoms.NetWorkarea);
        if (workAreaValues.Count >= 4)
        {
            var workArea = new Rectangle(
                checked((int)workAreaValues[0]),
                checked((int)workAreaValues[1]),
                checked((int)workAreaValues[2]),
                checked((int)workAreaValues[3]));
            return new MonitorInfo(displayBounds, workArea);
        }

        return new MonitorInfo(displayBounds, displayBounds);
    }

    public WindowStateSnapshot InspectWindowState(IntPtr windowHandle)
    {
        var bounds = GetWindowBounds(windowHandle);
        return new WindowStateSnapshot(bounds, InspectMonitorAt(new Point(bounds.Left, bounds.Top)));
    }

    private WindowTraits BuildTraits(
        IntPtr clientWindow,
        IntPtr frameWindow,
        Point screenPoint,
        Rectangle bounds,
        Rectangle frameBounds,
        MonitorInfo monitorInfo)
    {
        var atoms = _connection.Atoms;
        var stateAtoms = X11PropertyReader.ReadIntPtrArray(_connection, clientWindow, atoms.NetWmState, X11Native.AnyPropertyType);
        var typeAtoms = X11PropertyReader.ReadIntPtrArray(_connection, clientWindow, atoms.NetWmWindowType, X11Native.AnyPropertyType);
        var allowedActions = X11PropertyReader.ReadIntPtrArray(_connection, clientWindow, atoms.NetWmAllowedActions, X11Native.AnyPropertyType);

        return new WindowTraits(
            IsCaptionHit(clientWindow, frameWindow, screenPoint, bounds, frameBounds),
            IsViewable(clientWindow),
            allowedActions.Count == 0 || allowedActions.Contains(atoms.NetWmActionResize),
            stateAtoms.Contains(atoms.NetWmStateHidden),
            stateAtoms.Contains(atoms.NetWmStateMaximizedHorz) && stateAtoms.Contains(atoms.NetWmStateMaximizedVert),
            typeAtoms.Count == 0 || typeAtoms.Contains(atoms.NetWmWindowTypeNormal),
            stateAtoms.Contains(atoms.NetWmStateFullscreen) || IsFullscreen(bounds, monitorInfo.Bounds),
            false,
            false,
            false);
    }

    private static WindowInspectionResult CreateUnsupportedResult()
    {
        var traits = new WindowTraits(false, false, false, false, false, false, false, false, false, false);
        return new WindowInspectionResult(IntPtr.Zero, Rectangle.Empty, MonitorInfo.Empty, traits, WindowEligibilityResult.Unsupported(WindowEligibilityReason.Unknown, "No eligible X11 window was found under the pointer."));
    }

    private X11PointerSnapshot QueryPointer()
    {
        var success = X11Native.XQueryPointer(
            _connection.Display,
            _connection.RootWindow,
            out _,
            out var child,
            out var rootX,
            out var rootY,
            out _,
            out _,
            out var mask);

        return success == X11Native.False
            ? X11PointerSnapshot.Empty
            : new X11PointerSnapshot(child, new Point(rootX, rootY), mask);
    }

    private IntPtr FindClientWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (IsClientWindow(window))
        {
            return window;
        }

        if (X11Native.XQueryTree(
                _connection.Display,
                window,
                out _,
                out _,
                out var children,
                out var childrenCount) == X11Native.False || children == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            for (var index = 0; index < childrenCount; index++)
            {
                var child = System.Runtime.InteropServices.Marshal.ReadIntPtr(children, checked((int)index) * IntPtr.Size);
                var client = FindClientWindow(child);
                if (client != IntPtr.Zero)
                {
                    return client;
                }
            }
        }
        finally
        {
            X11Native.XFree(children);
        }

        return IntPtr.Zero;
    }

    private bool IsClientWindow(IntPtr window)
    {
        if (X11PropertyReader.ReadLongArray(_connection, window, _connection.Atoms.WmState).Count > 0)
        {
            return true;
        }

        return X11PropertyReader
            .ReadIntPtrArray(_connection, _connection.RootWindow, _connection.Atoms.NetClientList, X11Native.XaWindow.ToInt64())
            .Contains(window);
    }

    private Rectangle GetWindowBounds(IntPtr window)
    {
        if (window == IntPtr.Zero ||
            X11Native.XGetGeometry(
                _connection.Display,
                window,
                out _,
                out _,
                out _,
                out var width,
                out var height,
                out _,
                out _) == X11Native.False)
        {
            return Rectangle.Empty;
        }

        X11Native.XTranslateCoordinates(
            _connection.Display,
            window,
            _connection.RootWindow,
            0,
            0,
            out var rootX,
            out var rootY,
            out _);

        return new Rectangle(rootX, rootY, checked((int)width), checked((int)height));
    }

    private bool IsViewable(IntPtr window)
    {
        return X11Native.XGetWindowAttributes(_connection.Display, window, out var attributes) != X11Native.False &&
               attributes.MapState == X11Native.IsViewable;
    }

    private static bool IsCaptionHit(IntPtr clientWindow, IntPtr frameWindow, Point point, Rectangle clientBounds, Rectangle frameBounds)
    {
        if (frameWindow != clientWindow && frameBounds.Contains(point) && !clientBounds.Contains(point))
        {
            return point.Y < clientBounds.Top;
        }

        if (clientBounds == Rectangle.Empty || !clientBounds.Contains(point))
        {
            return false;
        }

        return point.Y < clientBounds.Top + Math.Min(FallbackTitleBarHeight, Math.Max(1, clientBounds.Height / 5));
    }

    private static bool IsFullscreen(Rectangle windowBounds, Rectangle monitorBounds)
    {
        if (windowBounds == Rectangle.Empty || monitorBounds == Rectangle.Empty)
        {
            return false;
        }

        return Math.Abs(windowBounds.Left - monitorBounds.Left) <= 1 &&
               Math.Abs(windowBounds.Top - monitorBounds.Top) <= 1 &&
               Math.Abs(windowBounds.Right - monitorBounds.Right) <= 1 &&
               Math.Abs(windowBounds.Bottom - monitorBounds.Bottom) <= 1;
    }

    private readonly record struct X11PointerSnapshot(IntPtr ChildWindow, Point Position, uint ButtonMask)
    {
        public static X11PointerSnapshot Empty { get; } = new(IntPtr.Zero, Point.Empty, 0);
    }
}
