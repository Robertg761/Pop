using System.Drawing;
using System.Reflection;
using Pop.App.Windows.Platform.Windowing;
using Forms = System.Windows.Forms;

namespace Pop.Tests;

public sealed class WindowInspectorTests
{
    [Fact]
    public void IsLikelyCaptionHit_ReturnsTrue_InsideCaptionBand()
    {
        var bounds = new Rectangle(100, 100, 1200, 800);
        var point = new Point(
            bounds.Left + Forms.SystemInformation.FrameBorderSize.Width + GetSystemMenuWidth() + 40,
            bounds.Top + Forms.SystemInformation.FrameBorderSize.Height + Math.Max(1, Forms.SystemInformation.CaptionHeight / 2));

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.True(result);
    }

    [Fact]
    public void IsLikelyCaptionHit_ReturnsTrue_InsideExtendedDragBand()
    {
        var bounds = new Rectangle(100, 100, 1200, 900);
        var point = new Point(
            bounds.Left + Forms.SystemInformation.FrameBorderSize.Width + GetSystemMenuWidth() + 40,
            bounds.Top + Forms.SystemInformation.FrameBorderSize.Height + Forms.SystemInformation.CaptionHeight + 12);

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.True(result);
    }

    [Fact]
    public void IsLikelyCaptionHit_ReturnsFalse_BelowExtendedDragBand()
    {
        var bounds = new Rectangle(100, 100, 1200, 800);
        var point = new Point(
            bounds.Left + Forms.SystemInformation.FrameBorderSize.Width + GetSystemMenuWidth() + 40,
            bounds.Top + Forms.SystemInformation.FrameBorderSize.Height + GetLikelyCaptionBandHeight(bounds) + 10);

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.False(result);
    }

    [Fact]
    public void IsLikelyCaptionHit_ReturnsFalse_InsideCaptionButtons()
    {
        var bounds = new Rectangle(100, 100, 1200, 800);
        var point = new Point(
            bounds.Right - Forms.SystemInformation.FrameBorderSize.Width - Math.Max(1, Forms.SystemInformation.CaptionButtonSize.Width / 2),
            bounds.Top + Forms.SystemInformation.FrameBorderSize.Height + Math.Max(1, Forms.SystemInformation.CaptionHeight / 2));

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.False(result);
    }

    [Fact]
    public void IsLikelyCaptionHit_ReturnsFalse_InsideSystemMenuArea()
    {
        var bounds = new Rectangle(100, 100, 1200, 800);
        var point = new Point(
            bounds.Left + Forms.SystemInformation.FrameBorderSize.Width + Math.Max(1, GetSystemMenuWidth() / 2),
            bounds.Top + Forms.SystemInformation.FrameBorderSize.Height + Math.Max(1, Forms.SystemInformation.CaptionHeight / 2));

        var result = InvokeLikelyCaptionHit(bounds, point);

        Assert.False(result);
    }

    private static bool InvokeLikelyCaptionHit(Rectangle bounds, Point point)
    {
        var method = typeof(WindowInspector).GetMethod("IsLikelyCaptionHit", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("Unable to find IsLikelyCaptionHit.");
        }

        return (bool)(method.Invoke(null, new object[] { bounds, point }) ?? false);
    }

    private static int GetLikelyCaptionBandHeight(Rectangle bounds)
    {
        return Math.Max(
            Math.Max(1, Forms.SystemInformation.CaptionHeight),
            Math.Min(72, Math.Max(Math.Max(1, Forms.SystemInformation.CaptionHeight), bounds.Height / 6)));
    }

    private static int GetSystemMenuWidth()
    {
        return Math.Max(
            Math.Max(1, Forms.SystemInformation.CaptionButtonSize.Width),
            Forms.SystemInformation.SmallIconSize.Width + Forms.SystemInformation.FrameBorderSize.Width);
    }
}
