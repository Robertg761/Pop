using System.Drawing;
using Pop.App.Linux.Platform.X11;

namespace Pop.Tests;

public sealed class X11WorkAreaCalculatorTests
{
    private static readonly Rectangle Display = new(0, 0, 3840, 1080);
    private static readonly Rectangle LeftMonitor = new(0, 0, 1920, 1080);
    private static readonly Rectangle RightMonitor = new(1920, 0, 1920, 1080);

    [Fact]
    public void ComputeWorkArea_NoStruts_ReturnsMonitorBounds()
    {
        var workArea = X11WorkAreaCalculator.ComputeWorkArea(LeftMonitor, Display, []);

        Assert.Equal(LeftMonitor, workArea);
    }

    [Fact]
    public void ComputeWorkArea_TopPanelOnLeftMonitor_DoesNotShaveRightMonitor()
    {
        // _NET_WM_STRUT_PARTIAL: 28px top panel spanning x 0..1919 only.
        var struts = new[] { new long[] { 0, 0, 28, 0, 0, 0, 0, 0, 0, 1919, 0, 0 } };

        var leftWorkArea = X11WorkAreaCalculator.ComputeWorkArea(LeftMonitor, Display, struts);
        var rightWorkArea = X11WorkAreaCalculator.ComputeWorkArea(RightMonitor, Display, struts);

        Assert.Equal(new Rectangle(0, 28, 1920, 1052), leftWorkArea);
        Assert.Equal(RightMonitor, rightWorkArea);
    }

    [Fact]
    public void ComputeWorkArea_FourValueStrut_SpansTheWholeDisplayEdge()
    {
        // _NET_WM_STRUT has no ranges: a 40px bottom panel reserves the full display width.
        var struts = new[] { new long[] { 0, 0, 0, 40 } };

        var leftWorkArea = X11WorkAreaCalculator.ComputeWorkArea(LeftMonitor, Display, struts);
        var rightWorkArea = X11WorkAreaCalculator.ComputeWorkArea(RightMonitor, Display, struts);

        Assert.Equal(new Rectangle(0, 0, 1920, 1040), leftWorkArea);
        Assert.Equal(new Rectangle(1920, 0, 1920, 1040), rightWorkArea);
    }

    [Fact]
    public void ComputeWorkArea_LeftDock_OnlyAffectsLeftMonitor()
    {
        var struts = new[] { new long[] { 64, 0, 0, 0, 0, 1079, 0, 0, 0, 0, 0, 0 } };

        var leftWorkArea = X11WorkAreaCalculator.ComputeWorkArea(LeftMonitor, Display, struts);
        var rightWorkArea = X11WorkAreaCalculator.ComputeWorkArea(RightMonitor, Display, struts);

        Assert.Equal(new Rectangle(64, 0, 1856, 1080), leftWorkArea);
        Assert.Equal(RightMonitor, rightWorkArea);
    }

    [Fact]
    public void ComputeWorkArea_StrutOutsideMonitorEdgeRange_IsIgnored()
    {
        // Stacked monitors; a left dock limited to the bottom monitor's y range.
        var stackedDisplay = new Rectangle(0, 0, 1920, 2160);
        var topMonitor = new Rectangle(0, 0, 1920, 1080);
        var bottomMonitor = new Rectangle(0, 1080, 1920, 1080);
        var struts = new[] { new long[] { 48, 0, 0, 0, 1080, 2159, 0, 0, 0, 0, 0, 0 } };

        var topWorkArea = X11WorkAreaCalculator.ComputeWorkArea(topMonitor, stackedDisplay, struts);
        var bottomWorkArea = X11WorkAreaCalculator.ComputeWorkArea(bottomMonitor, stackedDisplay, struts);

        Assert.Equal(topMonitor, topWorkArea);
        Assert.Equal(new Rectangle(48, 1080, 1872, 1080), bottomWorkArea);
    }

    [Fact]
    public void ComputeWorkArea_MultipleStruts_Combine()
    {
        var struts = new[]
        {
            new long[] { 0, 0, 28, 0, 0, 0, 0, 0, 0, 1919, 0, 0 },
            new long[] { 0, 0, 0, 40, 0, 0, 0, 0, 0, 0, 0, 1919 }
        };

        var workArea = X11WorkAreaCalculator.ComputeWorkArea(LeftMonitor, Display, struts);

        Assert.Equal(new Rectangle(0, 28, 1920, 1012), workArea);
    }

    [Fact]
    public void ComputeWorkArea_StrutReservingWholeMonitor_FallsBackToMonitorBounds()
    {
        var struts = new[] { new long[] { 1920, 0, 0, 0, 0, 1079, 0, 0, 0, 0, 0, 0 } };

        var workArea = X11WorkAreaCalculator.ComputeWorkArea(LeftMonitor, Display, struts);

        Assert.Equal(LeftMonitor, workArea);
    }

    [Fact]
    public void ComputeWorkArea_MalformedStrut_IsIgnored()
    {
        var struts = new[] { new long[] { 10, 20 } };

        var workArea = X11WorkAreaCalculator.ComputeWorkArea(LeftMonitor, Display, struts);

        Assert.Equal(LeftMonitor, workArea);
    }
}
