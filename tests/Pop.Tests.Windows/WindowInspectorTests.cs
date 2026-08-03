using System.Drawing;
using System.Reflection;
using Pop.App.Windows.Platform.Windowing;

namespace Pop.Tests;

public sealed class WindowInspectorTests
{
    // Fixed metrics (typical 100%-DPI values) so the heuristic is exercised deterministically
    // instead of depending on the machine's system DPI.
    private static readonly CaptionMetrics Metrics = new(
        FrameBorderWidth: 8,
        FrameBorderHeight: 8,
        CaptionHeight: 23,
        CaptionButtonWidth: 36,
        SmallIconWidth: 16);

    [Fact]
    public void IsLikelyCaptionHit_ReturnsTrue_InsideCaptionBand()
    {
        var bounds = new Rectangle(100, 100, 1200, 800);
        var point = new Point(
            bounds.Left + Metrics.FrameBorderWidth + GetSystemMenuWidth() + 40,
            bounds.Top + Metrics.FrameBorderHeight + Math.Max(1, Metrics.CaptionHeight / 2));

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.True(result);
    }

    [Fact]
    public void IsLikelyCaptionHit_ReturnsTrue_InsideExtendedDragBand()
    {
        var bounds = new Rectangle(100, 100, 1200, 900);
        var point = new Point(
            bounds.Left + Metrics.FrameBorderWidth + GetSystemMenuWidth() + 40,
            bounds.Top + Metrics.FrameBorderHeight + Metrics.CaptionHeight + 12);

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.True(result);
    }

    [Fact]
    public void IsLikelyCaptionHit_ReturnsFalse_BelowExtendedDragBand()
    {
        var bounds = new Rectangle(100, 100, 1200, 800);
        var point = new Point(
            bounds.Left + Metrics.FrameBorderWidth + GetSystemMenuWidth() + 40,
            bounds.Top + Metrics.FrameBorderHeight + GetLikelyCaptionBandHeight(bounds) + 10);

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.False(result);
    }

    [Fact]
    public void IsLikelyCaptionHit_ReturnsFalse_InsideCaptionButtons()
    {
        var bounds = new Rectangle(100, 100, 1200, 800);
        var point = new Point(
            bounds.Right - Metrics.FrameBorderWidth - Math.Max(1, Metrics.CaptionButtonWidth / 2),
            bounds.Top + Metrics.FrameBorderHeight + Math.Max(1, Metrics.CaptionHeight / 2));

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.False(result);
    }

    [Fact]
    public void IsLikelyCaptionHit_ReturnsFalse_InsideSystemMenuArea()
    {
        var bounds = new Rectangle(100, 100, 1200, 800);
        var point = new Point(
            bounds.Left + Metrics.FrameBorderWidth + Math.Max(1, GetSystemMenuWidth() / 2),
            bounds.Top + Metrics.FrameBorderHeight + Math.Max(1, Metrics.CaptionHeight / 2));

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.False(result);
    }

    private static bool InvokeLikelyCaptionHit(Rectangle bounds, Point point)
    {
        var method = typeof(WindowInspector).GetMethod(
            "IsLikelyCaptionHit",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(Rectangle), typeof(Point), typeof(CaptionMetrics)]);
        if (method is null)
        {
            throw new InvalidOperationException("Unable to find IsLikelyCaptionHit.");
        }

        return (bool)(method.Invoke(null, new object[] { bounds, point, Metrics }) ?? false);
    }

    private static int GetLikelyCaptionBandHeight(Rectangle bounds)
    {
        return Math.Max(
            Math.Max(1, Metrics.CaptionHeight),
            Math.Min(72, Math.Max(Math.Max(1, Metrics.CaptionHeight), bounds.Height / 6)));
    }

    private static int GetSystemMenuWidth()
    {
        return Math.Max(
            Math.Max(1, Metrics.CaptionButtonWidth),
            Metrics.SmallIconWidth + Metrics.FrameBorderWidth);
    }
}
