using System.Diagnostics;
using System.Drawing;
using Pop.Core.Interfaces;
using Pop.Core.Interop;

namespace Pop.Core.Services;

public sealed class WindowAnimator : IWindowAnimator
{
    public async Task AnimateToTileAsync(IntPtr windowHandle, Rectangle targetBounds, double releaseVelocityX, int durationMs, CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero || targetBounds == Rectangle.Empty)
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(windowHandle, out var currentRectStruct))
        {
            NativeMethods.MoveWindow(windowHandle, targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height, true);
            return;
        }

        var startRect = currentRectStruct.ToRectangle();
        if (durationMs <= 16)
        {
            NativeMethods.MoveWindow(windowHandle, targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height, true);
            return;
        }

        var overshootX = Math.Clamp((int)(releaseVelocityX * 0.045), -140, 140);
        var controlRect = new Rectangle(
            startRect.X + overshootX,
            startRect.Y + ((targetBounds.Y - startRect.Y) / 5),
            startRect.Width,
            startRect.Height);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < durationMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var t = stopwatch.Elapsed.TotalMilliseconds / durationMs;
            var eased = EaseOutCubic(t);
            var frame = InterpolateBezier(startRect, controlRect, targetBounds, eased);

            NativeMethods.MoveWindow(windowHandle, frame.X, frame.Y, frame.Width, frame.Height, true);
            await Task.Delay(16, cancellationToken);
        }

        NativeMethods.MoveWindow(windowHandle, targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height, true);
    }

    private static Rectangle InterpolateBezier(Rectangle start, Rectangle control, Rectangle end, double t)
    {
        return new Rectangle(
            Bezier(start.X, control.X, end.X, t),
            Bezier(start.Y, control.Y, end.Y, t),
            Math.Max(100, Bezier(start.Width, control.Width, end.Width, t)),
            Math.Max(100, Bezier(start.Height, control.Height, end.Height, t)));
    }

    private static int Bezier(double start, double control, double end, double t)
    {
        var inverse = 1 - t;
        var value = (inverse * inverse * start) + (2 * inverse * t * control) + (t * t * end);
        return (int)Math.Round(value);
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
}
