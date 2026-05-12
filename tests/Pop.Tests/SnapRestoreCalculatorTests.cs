using System.Drawing;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class SnapRestoreCalculatorTests
{
    [Fact]
    public void TryCreateRestoreBounds_RestoresPreviousSizeUnderDragPoint()
    {
        var currentBounds = new Rectangle(0, 0, 960, 1040);
        var previousBounds = new Rectangle(320, 140, 800, 600);
        var dragPoint = new Point(480, 18);

        var restored = SnapRestoreCalculator.TryCreateRestoreBounds(
            currentBounds,
            currentBounds,
            previousBounds,
            dragPoint,
            new Rectangle(0, 0, 1920, 1040),
            out var restoreBounds);

        Assert.True(restored);
        Assert.Equal(new Rectangle(80, 0, 800, 600), restoreBounds);
    }

    [Fact]
    public void TryCreateRestoreBounds_ReturnsFalse_WhenWindowNoLongerMatchesSnap()
    {
        var restored = SnapRestoreCalculator.TryCreateRestoreBounds(
            new Rectangle(300, 120, 820, 610),
            new Rectangle(0, 0, 960, 1040),
            new Rectangle(320, 140, 800, 600),
            new Point(500, 160),
            new Rectangle(0, 0, 1920, 1040),
            out var restoreBounds);

        Assert.False(restored);
        Assert.Equal(Rectangle.Empty, restoreBounds);
    }

    [Fact]
    public void AreBoundsClose_AllowsSmallWindowManagerDeltas()
    {
        Assert.True(SnapRestoreCalculator.AreBoundsClose(
            new Rectangle(2, 3, 956, 1034),
            new Rectangle(0, 0, 960, 1040)));
    }
}
