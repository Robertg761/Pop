using System.Drawing;
using Pop.App.Linux.Platform.X11;

namespace Pop.Tests;

public sealed class X11FrameGeometryTests
{
    [Fact]
    public void ToClientBounds_NoExtents_ReturnsFrameBounds()
    {
        var frameBounds = new Rectangle(0, 28, 960, 1052);

        var clientBounds = X11FrameGeometry.ToClientBounds(frameBounds, X11FrameExtents.None);

        Assert.Equal(frameBounds, clientBounds);
    }

    [Fact]
    public void ToClientBounds_InsetsClientSoFrameFillsTarget()
    {
        // Server-side decorations: 4px borders and a 32px title bar.
        var frameBounds = new Rectangle(0, 28, 960, 1052);
        var extents = new X11FrameExtents(4, 4, 32, 4);

        var clientBounds = X11FrameGeometry.ToClientBounds(frameBounds, extents);

        Assert.Equal(new Rectangle(4, 60, 952, 1016), clientBounds);
    }

    [Fact]
    public void ToClientBounds_OversizedExtents_ClampToMinimumSize()
    {
        var frameBounds = new Rectangle(0, 0, 100, 100);
        var extents = new X11FrameExtents(500, 500, 500, 500);

        var clientBounds = X11FrameGeometry.ToClientBounds(frameBounds, extents);

        Assert.Equal(1, clientBounds.Width);
        Assert.Equal(1, clientBounds.Height);
    }

    [Fact]
    public void ToClientBounds_NegativeExtents_AreTreatedAsZero()
    {
        var frameBounds = new Rectangle(10, 10, 500, 400);
        var extents = new X11FrameExtents(-4, -4, -32, -4);

        var clientBounds = X11FrameGeometry.ToClientBounds(frameBounds, extents);

        Assert.Equal(frameBounds, clientBounds);
    }
}
