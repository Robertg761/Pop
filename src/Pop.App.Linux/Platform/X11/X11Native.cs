using System.Runtime.InteropServices;

namespace Pop.App.Linux.Platform.X11;

internal static partial class X11Native
{
    public const int False = 0;
    public const int Success = 0;
    public const int None = 0;
    public const int CurrentTime = 0;
    public const int SubstructureRedirectMask = 1 << 20;
    public const int SubstructureNotifyMask = 1 << 19;
    public const int ClientMessage = 33;
    public const int PropModeReplace = 0;
    public const int Button1Mask = 1 << 8;
    public const int ControlMask = 1 << 2;
    public const long AnyPropertyType = 0;
    public const int IsUnmapped = 0;
    public const int IsViewable = 2;

    public static readonly IntPtr XaCardinal = new(6);
    public static readonly IntPtr XaWindow = new(33);

    [DllImport("libX11.so.6")]
    public static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XDisplayWidth(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6")]
    public static extern int XDisplayHeight(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6")]
    public static extern IntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists);

    [DllImport("libX11.so.6")]
    public static extern int XQueryPointer(
        IntPtr display,
        IntPtr window,
        out IntPtr rootReturn,
        out IntPtr childReturn,
        out int rootXReturn,
        out int rootYReturn,
        out int winXReturn,
        out int winYReturn,
        out uint maskReturn);

    [DllImport("libX11.so.6")]
    public static extern int XQueryTree(
        IntPtr display,
        IntPtr window,
        out IntPtr rootReturn,
        out IntPtr parentReturn,
        out IntPtr childrenReturn,
        out uint childrenCountReturn);

    [DllImport("libX11.so.6")]
    public static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes attributes);

    [DllImport("libX11.so.6")]
    public static extern int XGetGeometry(
        IntPtr display,
        IntPtr drawable,
        out IntPtr rootReturn,
        out int xReturn,
        out int yReturn,
        out uint widthReturn,
        out uint heightReturn,
        out uint borderWidthReturn,
        out uint depthReturn);

    [DllImport("libX11.so.6")]
    public static extern int XTranslateCoordinates(
        IntPtr display,
        IntPtr sourceWindow,
        IntPtr destinationWindow,
        int sourceX,
        int sourceY,
        out int destinationXReturn,
        out int destinationYReturn,
        out IntPtr childReturn);

    [DllImport("libX11.so.6")]
    public static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr longOffset,
        IntPtr longLength,
        int delete,
        IntPtr reqType,
        out IntPtr actualTypeReturn,
        out int actualFormatReturn,
        out nuint itemsCountReturn,
        out nuint bytesAfterReturn,
        out IntPtr propReturn);

    [DllImport("libX11.so.6")]
    public static extern int XSendEvent(
        IntPtr display,
        IntPtr window,
        int propagate,
        IntPtr eventMask,
        ref XClientMessageEvent eventSend);

    [DllImport("libX11.so.6")]
    public static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XFree(IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    public struct XWindowAttributes
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int BorderWidth;
        public int Depth;
        public IntPtr Visual;
        public IntPtr Root;
        public int Class;
        public int BitGravity;
        public int WinGravity;
        public int BackingStore;
        public nuint BackingPlanes;
        public nuint BackingPixel;
        public int SaveUnder;
        public IntPtr Colormap;
        public int MapInstalled;
        public int MapState;
        public IntPtr AllEventMasks;
        public IntPtr YourEventMask;
        public IntPtr DoNotPropagateMask;
        public int OverrideRedirect;
        public IntPtr Screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XClientMessageEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public IntPtr Window;
        public IntPtr MessageType;
        public int Format;
        public IntPtr Data0;
        public IntPtr Data1;
        public IntPtr Data2;
        public IntPtr Data3;
        public IntPtr Data4;
    }
}
