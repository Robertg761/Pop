using System.Drawing;

namespace Pop.App.Linux.Platform.X11;

internal readonly record struct X11FrameExtents(long Left, long Right, long Top, long Bottom)
{
    public static X11FrameExtents None { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Converts frame-rectangle targets into client-window geometry for _NET_MOVERESIZE_WINDOW.
/// With StaticGravity the request positions the CLIENT window, but snap targets describe where
/// the decorated FRAME should land; without this inset a server-side title bar ends up pushed
/// under the top panel.
/// </summary>
internal static class X11FrameGeometry
{
    public static Rectangle ToClientBounds(Rectangle frameBounds, X11FrameExtents extents)
    {
        if (extents == X11FrameExtents.None)
        {
            return frameBounds;
        }

        var left = (int)Math.Clamp(extents.Left, 0, int.MaxValue);
        var right = (int)Math.Clamp(extents.Right, 0, int.MaxValue);
        var top = (int)Math.Clamp(extents.Top, 0, int.MaxValue);
        var bottom = (int)Math.Clamp(extents.Bottom, 0, int.MaxValue);

        return new Rectangle(
            frameBounds.X + left,
            frameBounds.Y + top,
            (int)Math.Max(1, frameBounds.Width - (long)left - right),
            (int)Math.Max(1, frameBounds.Height - (long)top - bottom));
    }
}
