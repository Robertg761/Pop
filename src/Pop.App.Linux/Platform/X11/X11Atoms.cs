namespace Pop.App.Linux.Platform.X11;

internal sealed class X11Atoms
{
    public X11Atoms(IntPtr display)
    {
        NetActiveWindow = Intern(display, "_NET_ACTIVE_WINDOW");
        NetClientList = Intern(display, "_NET_CLIENT_LIST");
        NetFrameExtents = Intern(display, "_NET_FRAME_EXTENTS");
        NetMoveresizeWindow = Intern(display, "_NET_MOVERESIZE_WINDOW");
        NetSupported = Intern(display, "_NET_SUPPORTED");
        NetWmAllowedActions = Intern(display, "_NET_WM_ALLOWED_ACTIONS");
        NetWmActionResize = Intern(display, "_NET_WM_ACTION_RESIZE");
        NetWmState = Intern(display, "_NET_WM_STATE");
        NetWmStateFullscreen = Intern(display, "_NET_WM_STATE_FULLSCREEN");
        NetWmStateHidden = Intern(display, "_NET_WM_STATE_HIDDEN");
        NetWmStateMaximizedHorz = Intern(display, "_NET_WM_STATE_MAXIMIZED_HORZ");
        NetWmStateMaximizedVert = Intern(display, "_NET_WM_STATE_MAXIMIZED_VERT");
        NetWmWindowType = Intern(display, "_NET_WM_WINDOW_TYPE");
        NetWmWindowTypeNormal = Intern(display, "_NET_WM_WINDOW_TYPE_NORMAL");
        NetWorkarea = Intern(display, "_NET_WORKAREA");
        WmState = Intern(display, "WM_STATE");
    }

    public IntPtr NetActiveWindow { get; }

    public IntPtr NetClientList { get; }

    public IntPtr NetFrameExtents { get; }

    public IntPtr NetMoveresizeWindow { get; }

    public IntPtr NetSupported { get; }

    public IntPtr NetWmAllowedActions { get; }

    public IntPtr NetWmActionResize { get; }

    public IntPtr NetWmState { get; }

    public IntPtr NetWmStateFullscreen { get; }

    public IntPtr NetWmStateHidden { get; }

    public IntPtr NetWmStateMaximizedHorz { get; }

    public IntPtr NetWmStateMaximizedVert { get; }

    public IntPtr NetWmWindowType { get; }

    public IntPtr NetWmWindowTypeNormal { get; }

    public IntPtr NetWorkarea { get; }

    public IntPtr WmState { get; }

    private static IntPtr Intern(IntPtr display, string name) => X11Native.XInternAtom(display, name, X11Native.False);
}
