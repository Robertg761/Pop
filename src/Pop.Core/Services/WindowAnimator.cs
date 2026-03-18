using System.Drawing;
using Pop.Core.Models;

namespace Pop.Core.Services;

public sealed class WindowAnimator
{
    private const double TargetFrameRate = 120d;

    public AnimationPlan CreatePlan(Rectangle startBounds, Rectangle targetBounds, double releaseVelocityX, int durationMs)
    {
        if (startBounds == Rectangle.Empty || targetBounds == Rectangle.Empty)
        {
            return new AnimationPlan(Array.Empty<AnimationFrame>(), targetBounds, Math.Max(0, durationMs), 0);
        }

        if (durationMs <= 16)
        {
            return new AnimationPlan([new AnimationFrame(TimeSpan.Zero, targetBounds)], targetBounds, durationMs, 0);
        }

        var frameCount = Math.Max(2, (int)Math.Ceiling((durationMs / 1000d) * TargetFrameRate));
        var maxOvershootPx = CalculateOvershoot(startBounds, targetBounds, releaseVelocityX);
        var frames = new List<AnimationFrame>(frameCount);

        for (var index = 0; index < frameCount; index++)
        {
            var progress = frameCount == 1 ? 1d : index / (double)(frameCount - 1);
            var offsetMs = (int)Math.Round(progress * durationMs);
            frames.Add(new AnimationFrame(TimeSpan.FromMilliseconds(offsetMs), InterpolateFrame(startBounds, targetBounds, progress, maxOvershootPx)));
        }

        frames[^1] = new AnimationFrame(TimeSpan.FromMilliseconds(durationMs), targetBounds);
        return new AnimationPlan(frames, targetBounds, durationMs, maxOvershootPx);
    }
    private static Rectangle InterpolateFrame(Rectangle start, Rectangle end, double progress, int maxOvershootPx)
    {
        var rawX = Lerp(start.X, end.X, EaseOutBack(progress, CalculateOvershootFactor(maxOvershootPx)));
        var clampedX = ClampOvershoot(rawX, start.X, end.X, maxOvershootPx);
        var sizeProgress = EaseOutCubic(progress);
        return new Rectangle(
            clampedX,
            Lerp(start.Y, end.Y, EaseOutCubic(progress)),
            Math.Max(100, Lerp(start.Width, end.Width, sizeProgress)),
            Math.Max(100, Lerp(start.Height, end.Height, sizeProgress)));
    }

    private static int CalculateOvershoot(Rectangle start, Rectangle end, double releaseVelocityX)
    {
        var desired = (int)Math.Round(Math.Abs(releaseVelocityX) * 0.018);
        var distance = Math.Abs(end.X - start.X);
        var maxByDistance = Math.Max(24, Math.Min(96, distance / 6));
        return Math.Min(maxByDistance, Math.Clamp(desired, 18, 96));
    }

    private static double CalculateOvershootFactor(int maxOvershootPx)
    {
        var normalized = Math.Clamp(Math.Abs(maxOvershootPx) / 96d, 0d, 1d);
        return 1.05 + (normalized * 1.15);
    }

    private static int Lerp(double start, double end, double t) => (int)Math.Round(start + ((end - start) * t));

    private static int ClampOvershoot(int value, int start, int end, int maxOvershootPx)
    {
        if (maxOvershootPx == 0)
        {
            return value;
        }

        return end >= start
            ? Math.Clamp(value, Math.Min(start, end) - Math.Abs(maxOvershootPx), end + Math.Abs(maxOvershootPx))
            : Math.Clamp(value, end - Math.Abs(maxOvershootPx), Math.Max(start, end) + Math.Abs(maxOvershootPx));
    }

    private static double EaseOutBack(double t, double overshootFactor)
    {
        var x = t - 1;
        return 1 + ((overshootFactor + 1) * Math.Pow(x, 3)) + (overshootFactor * Math.Pow(x, 2));
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
}
