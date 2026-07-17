using System.Drawing;
using System.Runtime.InteropServices;
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
    private IReadOnlyList<Rectangle> _cachedScreens = Array.Empty<Rectangle>();
    private Rectangle _cachedDisplayBounds;
    private Rectangle _cachedGlobalWorkArea;
    private DateTimeOffset _cachedLayoutValidUntil;
    private bool _xineramaUnavailable;

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
        RefreshLayoutIfStale(DateTimeOffset.UtcNow);

        // Use per-monitor geometry only when Xinerama reports more than one head, so single-head
        // setups keep the proven whole-display path untouched.
        if (_cachedScreens.Count > 1)
        {
            var monitorBounds = SelectMonitor(_cachedScreens, screenPoint);
            return new MonitorInfo(monitorBounds, IntersectWorkArea(_cachedGlobalWorkArea, monitorBounds));
        }

        return new MonitorInfo(_cachedDisplayBounds, _cachedGlobalWorkArea);
    }

    private void RefreshLayoutIfStale(DateTimeOffset now)
    {
        if (now < _cachedLayoutValidUntil && _cachedDisplayBounds != Rectangle.Empty)
        {
            return;
        }

        _cachedDisplayBounds = new Rectangle(0, 0, GetDisplayWidth(), GetDisplayHeight());
        _cachedGlobalWorkArea = ReadGlobalWorkArea(_cachedDisplayBounds);
        _cachedScreens = QueryXineramaScreens();
        _cachedLayoutValidUntil = now + MonitorInfoCacheDuration;
    }

    private Rectangle ReadGlobalWorkArea(Rectangle displayBounds)
    {
        var workAreaValues = X11PropertyReader.ReadLongArray(_connection, _connection.RootWindow, _connection.Atoms.NetWorkarea);
        if (workAreaValues.Count >= 4)
        {
            return new Rectangle(
                checked((int)workAreaValues[0]),
                checked((int)workAreaValues[1]),
                checked((int)workAreaValues[2]),
                checked((int)workAreaValues[3]));
        }

        return displayBounds;
    }

    private IReadOnlyList<Rectangle> QueryXineramaScreens()
    {
        if (_xineramaUnavailable)
        {
            return Array.Empty<Rectangle>();
        }

        try
        {
            lock (_connection.SyncRoot)
            {
                if (_connection.IsDisposed || X11Native.XineramaIsActive(_connection.Display) == 0)
                {
                    return Array.Empty<Rectangle>();
                }

                var pointer = X11Native.XineramaQueryScreens(_connection.Display, out var count);
                if (pointer == IntPtr.Zero || count <= 0)
                {
                    if (pointer != IntPtr.Zero)
                    {
                        X11Native.XFree(pointer);
                    }

                    return Array.Empty<Rectangle>();
                }

                try
                {
                    var size = Marshal.SizeOf<X11Native.XineramaScreenInfo>();
                    var screens = new List<Rectangle>(count);
                    for (var index = 0; index < count; index++)
                    {
                        var info = Marshal.PtrToStructure<X11Native.XineramaScreenInfo>(pointer + (index * size));
                        if (info.Width > 0 && info.Height > 0)
                        {
                            screens.Add(new Rectangle(info.XOrg, info.YOrg, info.Width, info.Height));
                        }
                    }

                    return screens;
                }
                finally
                {
                    X11Native.XFree(pointer);
                }
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // libXinerama is not installed; fall back to whole-display geometry for the session.
            _xineramaUnavailable = true;
            return Array.Empty<Rectangle>();
        }
    }

    private static Rectangle SelectMonitor(IReadOnlyList<Rectangle> screens, Point point)
    {
        foreach (var screen in screens)
        {
            if (screen.Contains(point))
            {
                return screen;
            }
        }

        var best = screens[0];
        var bestDistance = long.MaxValue;
        foreach (var screen in screens)
        {
            var distance = DistanceSquared(point, screen);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = screen;
            }
        }

        return best;
    }

    private static long DistanceSquared(Point point, Rectangle rectangle)
    {
        long dx = point.X < rectangle.Left ? rectangle.Left - point.X
            : point.X > rectangle.Right ? point.X - rectangle.Right
            : 0;
        long dy = point.Y < rectangle.Top ? rectangle.Top - point.Y
            : point.Y > rectangle.Bottom ? point.Y - rectangle.Bottom
            : 0;
        return (dx * dx) + (dy * dy);
    }

    private static Rectangle IntersectWorkArea(Rectangle globalWorkArea, Rectangle monitorBounds)
    {
        var intersection = Rectangle.Intersect(globalWorkArea, monitorBounds);
        return intersection.Width > 0 && intersection.Height > 0 ? intersection : monitorBounds;
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
