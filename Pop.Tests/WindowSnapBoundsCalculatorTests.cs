using System.Drawing;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class WindowSnapBoundsCalculatorTests
{
    [Fact]
    public void AdjustWindowBoundsForVisibleTarget_ExpandsToCoverInvisibleResizeFrame()
    {
        var visibleTargetBounds = new Rectangle(0, 0, 960, 1040);
        var windowBounds = Rectangle.FromLTRB(92, 0, 1008, 1048);
        var visibleWindowBounds = Rectangle.FromLTRB(100, 0, 1000, 1040);

        var adjustedBounds = WindowSnapBoundsCalculator.AdjustWindowBoundsForVisibleTarget(
            visibleTargetBounds,
            windowBounds,
            visibleWindowBounds);

        Assert.Equal(Rectangle.FromLTRB(-8, 0, 968, 1048), adjustedBounds);
    }

    [Fact]
    public void AdjustWindowBoundsForVisibleTarget_FallsBackWhenInsetsAreInvalid()
    {
        var visibleTargetBounds = new Rectangle(960, 0, 960, 1040);
        var windowBounds = Rectangle.FromLTRB(100, 0, 1860, 1040);
        var visibleWindowBounds = Rectangle.FromLTRB(92, 0, 1868, 1048);

        var adjustedBounds = WindowSnapBoundsCalculator.AdjustWindowBoundsForVisibleTarget(
            visibleTargetBounds,
            windowBounds,
            visibleWindowBounds);

        Assert.Equal(visibleTargetBounds, adjustedBounds);
    }
}
