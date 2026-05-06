namespace Pop.App.Linux.Platform.X11;

public sealed class X11DisplayConnection : IDisposable
{
    private X11DisplayConnection(IntPtr display)
    {
        Display = display;
        RootWindow = X11Native.XDefaultRootWindow(display);
        Screen = X11Native.XDefaultScreen(display);
        Atoms = new X11Atoms(display);
    }

    public IntPtr Display { get; }

    public IntPtr RootWindow { get; }

    public int Screen { get; }

    internal X11Atoms Atoms { get; }

    public static X11DisplayConnection Open()
    {
        var display = X11Native.XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to open the X11 display. Pop for Linux currently requires an X11 session with DISPLAY set.");
        }

        return new X11DisplayConnection(display);
    }

    public void Dispose()
    {
        if (Display != IntPtr.Zero)
        {
            X11Native.XCloseDisplay(Display);
        }
    }
}
