namespace Pop.App.Linux.Platform.X11;

public sealed class X11DisplayConnection : IDisposable
{
    // Held in static fields so the GC never collects the delegates while Xlib holds the pointers.
    private static readonly X11Native.XErrorHandler ErrorHandler = HandleXError;
    private static readonly X11Native.XIOErrorHandler IOErrorHandler = HandleXIOError;
    private static int _errorHandlerInstalled;

    private bool _disposed;

    private X11DisplayConnection(IntPtr display)
    {
        Display = display;
        RootWindow = X11Native.XDefaultRootWindow(display);
        Screen = X11Native.XDefaultScreen(display);
        Atoms = new X11Atoms(display);
    }

    private static int HandleXError(IntPtr display, IntPtr errorEvent)
    {
        // Swallow asynchronous protocol errors (e.g. BadWindow from a window destroyed mid-drag).
        // Xlib's default handler prints and calls exit(); returning 0 keeps Pop alive instead.
        return 0;
    }

    private static int HandleXIOError(IntPtr display)
    {
        // Xlib calls this when the X connection is lost and requires that the handler never
        // return (the default calls exit(), bypassing all managed cleanup). Log a diagnosable
        // message, then exit through the runtime for an orderly process teardown.
        Console.Error.WriteLine("Pop lost its connection to the X server and must exit.");
        Environment.Exit(1);
        return 0;
    }

    public IntPtr Display { get; }

    public IntPtr RootWindow { get; }

    public int Screen { get; }

    internal X11Atoms Atoms { get; }

    internal object SyncRoot { get; } = new();

    public static X11DisplayConnection Open()
    {
        var display = X11Native.XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to open the X11 display. Pop for Linux currently requires an X11 session with DISPLAY set.");
        }

        // Install process-wide error handlers once so a stale window handle cannot exit() Pop
        // and a dropped X connection cannot bypass an orderly process exit.
        if (Interlocked.Exchange(ref _errorHandlerInstalled, 1) == 0)
        {
            try
            {
                X11Native.XSetErrorHandler(ErrorHandler);
                X11Native.XSetIOErrorHandler(IOErrorHandler);
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                Interlocked.Exchange(ref _errorHandlerInstalled, 0);
            }
        }

        return new X11DisplayConnection(display);
    }

    public void Dispose()
    {
        lock (SyncRoot)
        {
            if (_disposed || Display == IntPtr.Zero)
            {
                return;
            }

            _disposed = true;
            X11Native.XCloseDisplay(Display);
        }
    }

    // True once the connection has been closed; callers on the drag hot path check this before
    // issuing further Xlib calls to avoid using a freed Display during shutdown.
    public bool IsDisposed
    {
        get
        {
            lock (SyncRoot)
            {
                return _disposed;
            }
        }
    }
}
