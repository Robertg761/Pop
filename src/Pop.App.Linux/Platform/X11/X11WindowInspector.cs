using System.Drawing;
using Pop.Core.Models;
using Pop.Core.Services;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Linux.Platform.X11;

public sealed class X11WindowInspector : IWindowInspector
{
    private const int FallbackTitleBarHeight = 48;
    private static readonly TimeSpan MonitorInfoCacheDuration = TimeSpan.FromMilliseconds(250);
    private readonly X11DisplayConnection _connection;
    private readonly WindowEligibilityEvaluator _evaluator;
    private MonitorInfo _cachedMonitorInfo;
    private DateTimeOffset _cachedMonitorInfoValidUntil;

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
        var clientWindow = FindClientWindow(frameWindow, new ClientWindowLookup(_connection));
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
        var now = DateTimeOffset.UtcNow;
        if (_cachedMonitorInfo != MonitorInfo.Empty && now < _cachedMonitorInfoValidUntil)
        {
            return _cachedMonitorInfo;
        }

        var displayBounds = new Rectangle(
            0,
            0,
            GetDisplayWidth(),
            GetDisplayHeight());

        var workAreaValues = X11PropertyReader.ReadLongArray(_connection, _connection.RootWindow, _connection.Atoms.NetWorkarea);
        MonitorInfo monitorInfo;
        if (workAreaValues.Count >= 4)
        {
            var workArea = new Rectangle(
                checked((int)workAreaValues[0]),
                checked((int)workAreaValues[1]),
                checked((int)workAreaValues[2]),
                checked((int)workAreaValues[3]));
            monitorInfo = new MonitorInfo(displayBounds, workArea);
        }
        else
        {
            monitorInfo = new MonitorInfo(displayBounds, displayBounds);
        }

        _cachedMonitorInfo = monitorInfo;
        _cachedMonitorInfoValidUntil = now + MonitorInfoCacheDuration;
        return monitorInfo;
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
        int success;
        IntPtr child;
        int rootX;
        int rootY;
        uint mask;
        lock (_connection.SyncRoot)
        {
            success = X11Native.XQueryPointer(
                _connection.Display,
                _connection.RootWindow,
                out _,
                out child,
                out rootX,
                out rootY,
                out _,
                out _,
                out mask);
        }

        return success == X11Native.False
            ? X11PointerSnapshot.Empty
            : new X11PointerSnapshot(child, new Point(rootX, rootY), mask);
    }

    private IntPtr FindClientWindow(IntPtr window, ClientWindowLookup clientWindowLookup)
    {
        if (window == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (IsClientWindow(window, clientWindowLookup))
        {
            return window;
        }

        int queryTreeResult;
        IntPtr children;
        uint childrenCount;
        lock (_connection.SyncRoot)
        {
            queryTreeResult = X11Native.XQueryTree(
                _connection.Display,
                window,
                out _,
                out _,
                out children,
                out childrenCount);
        }

        if (queryTreeResult == X11Native.False || children == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            for (var index = 0; index < childrenCount; index++)
            {
                var child = System.Runtime.InteropServices.Marshal.ReadIntPtr(children, checked((int)index) * IntPtr.Size);
                var client = FindClientWindow(child, clientWindowLookup);
                if (client != IntPtr.Zero)
                {
                    return client;
                }
            }
        }
        finally
        {
            lock (_connection.SyncRoot)
            {
                X11Native.XFree(children);
            }
        }

        return IntPtr.Zero;
    }

    private bool IsClientWindow(IntPtr window, ClientWindowLookup clientWindowLookup)
    {
        if (X11PropertyReader.ReadLongArray(_connection, window, _connection.Atoms.WmState).Count > 0)
        {
            return true;
        }

        return clientWindowLookup.Contains(window);
    }

    private Rectangle GetWindowBounds(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return Rectangle.Empty;
        }

        int geometryResult;
        uint width;
        uint height;
        int rootX;
        int rootY;
        lock (_connection.SyncRoot)
        {
            geometryResult = X11Native.XGetGeometry(
                _connection.Display,
                window,
                out _,
                out _,
                out _,
                out width,
                out height,
                out _,
                out _);

            if (geometryResult != X11Native.False)
            {
                X11Native.XTranslateCoordinates(
                    _connection.Display,
                    window,
                    _connection.RootWindow,
                    0,
                    0,
                    out rootX,
                    out rootY,
                    out _);
            }
            else
            {
                rootX = 0;
                rootY = 0;
            }
        }

        if (geometryResult == X11Native.False)
        {
            return Rectangle.Empty;
        }

        return new Rectangle(rootX, rootY, checked((int)width), checked((int)height));
    }

    private bool IsViewable(IntPtr window)
    {
        int result;
        X11Native.XWindowAttributes attributes;
        lock (_connection.SyncRoot)
        {
            result = X11Native.XGetWindowAttributes(_connection.Display, window, out attributes);
        }

        return result != X11Native.False && attributes.MapState == X11Native.IsViewable;
    }

    private int GetDisplayWidth()
    {
        lock (_connection.SyncRoot)
        {
            return X11Native.XDisplayWidth(_connection.Display, _connection.Screen);
        }
    }

    private int GetDisplayHeight()
    {
        lock (_connection.SyncRoot)
        {
            return X11Native.XDisplayHeight(_connection.Display, _connection.Screen);
        }
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

    private sealed class ClientWindowLookup
    {
        private readonly X11DisplayConnection _connection;
        private HashSet<IntPtr>? _clientWindows;

        public ClientWindowLookup(X11DisplayConnection connection)
        {
            _connection = connection;
        }

        public bool Contains(IntPtr window)
        {
            _clientWindows ??= X11PropertyReader
                .ReadIntPtrArray(_connection, _connection.RootWindow, _connection.Atoms.NetClientList, X11Native.XaWindow.ToInt64())
                .ToHashSet();

            return _clientWindows.Contains(window);
        }
    }
}
