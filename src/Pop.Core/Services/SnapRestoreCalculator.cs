using System.Drawing;

namespace Pop.Core.Services;

public static class SnapRestoreCalculator
{
    public const int DefaultSnapTolerancePx = 96;
    private const int MinimumVisibleWidth = 120;
    private const int MinimumVisibleHeight = 40;

    public static bool TryCreateRestoreBounds(
        Rectangle currentBounds,
        Rectangle snappedBounds,
        Rectangle previousBounds,
        Point dragPoint,
        Rectangle workArea,
        out Rectangle restoreBounds,
        int snapTolerancePx = DefaultSnapTolerancePx)
    {
        restoreBounds = Rectangle.Empty;

        if (currentBounds == Rectangle.Empty ||
            snappedBounds == Rectangle.Empty ||
            previousBounds == Rectangle.Empty ||
            previousBounds.Width <= 0 ||
            previousBounds.Height <= 0 ||
            !AreBoundsClose(currentBounds, snappedBounds, snapTolerancePx))
        {
            return false;
        }

        var relativeX = currentBounds.Width <= 0
            ? 0.5d
            : (dragPoint.X - currentBounds.Left) / (double)currentBounds.Width;
        var offsetX = (int)Math.Round(Math.Clamp(relativeX, 0d, 1d) * previousBounds.Width);
        var offsetY = Math.Clamp(dragPoint.Y - currentBounds.Top, 0, Math.Max(0, previousBounds.Height - 1));

        restoreBounds = KeepReachable(
            new Rectangle(
                dragPoint.X - offsetX,
                dragPoint.Y - offsetY,
                previousBounds.Width,
                previousBounds.Height),
            workArea);

        return true;
    }

    public static bool AreBoundsClose(Rectangle first, Rectangle second, int tolerancePx = DefaultSnapTolerancePx)
    {
        if (first == Rectangle.Empty || second == Rectangle.Empty)
        {
            return false;
        }

        var tolerance = Math.Max(0, tolerancePx);
        return Math.Abs(first.Left - second.Left) <= tolerance &&
               Math.Abs(first.Top - second.Top) <= tolerance &&
               Math.Abs(first.Right - second.Right) <= tolerance &&
               Math.Abs(first.Bottom - second.Bottom) <= tolerance;
    }

    private static Rectangle KeepReachable(Rectangle bounds, Rectangle workArea)
    {
        if (workArea == Rectangle.Empty)
        {
            return bounds;
        }

        var minX = workArea.Left - bounds.Width + Math.Min(MinimumVisibleWidth, workArea.Width);
        var maxX = workArea.Right - Math.Min(MinimumVisibleWidth, workArea.Width);
        var minY = workArea.Top;
        var maxY = Math.Max(workArea.Top, workArea.Bottom - Math.Min(MinimumVisibleHeight, workArea.Height));

        return new Rectangle(
            ClampWhenOrdered(bounds.X, minX, maxX),
            ClampWhenOrdered(bounds.Y, minY, maxY),
            bounds.Width,
            bounds.Height);
    }

    private static int ClampWhenOrdered(int value, int minimum, int maximum)
    {
        return minimum <= maximum
            ? Math.Clamp(value, minimum, maximum)
            : value;
    }
}
