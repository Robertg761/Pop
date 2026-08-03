using System.Drawing;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class TileLayoutCalculatorTests
{
    [Fact]
    public void GetTileBounds_SplitsOddWidthAcrossLeftAndRight()
    {
        var monitor = new MonitorInfo(new Rectangle(0, 0, 1921, 1080), new Rectangle(0, 0, 1921, 1040));

        var leftBounds = TileLayoutCalculator.GetTileBounds(SnapTarget.LeftHalf, monitor);
        var rightBounds = TileLayoutCalculator.GetTileBounds(SnapTarget.RightHalf, monitor);

        Assert.Equal(new Rectangle(0, 0, 960, 1040), leftBounds);
        Assert.Equal(new Rectangle(960, 0, 961, 1040), rightBounds);
    }

    [Theory]
    [InlineData(0, 1040)]
    [InlineData(1920, 0)]
    [InlineData(-1920, 1040)]
    [InlineData(1920, -1040)]
    public void GetTileBounds_ReturnsEmpty_WhenWorkAreaIsDegenerate(int width, int height)
    {
        var monitor = new MonitorInfo(new Rectangle(0, 0, 1920, 1080), new Rectangle(100, 0, width, height));

        Assert.Equal(Rectangle.Empty, TileLayoutCalculator.GetTileBounds(SnapTarget.LeftHalf, monitor));
        Assert.Equal(Rectangle.Empty, TileLayoutCalculator.GetTileBounds(SnapTarget.RightHalf, monitor));
    }
}
