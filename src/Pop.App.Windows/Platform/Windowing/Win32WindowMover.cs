using System.Diagnostics;
using System.Drawing;
using Pop.App.Windows.Platform.Interop;
using Pop.Core.Models;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Windows.Platform.Windowing;

public sealed class Win32WindowMover : IWindowMover
{
    public async Task MoveWindowAsync(IntPtr windowHandle, AnimationPlan plan, CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero || plan.FinalBounds == Rectangle.Empty)
        {
            return;
        }

        if (plan.Frames.Count == 0)
        {
            NativeMethods.MoveWindow(windowHandle, plan.FinalBounds.X, plan.FinalBounds.Y, plan.FinalBounds.Width, plan.FinalBounds.Height, true);
            return;
        }

        Rectangle? previousBounds = null;
        var stopwatch = Stopwatch.StartNew();
        foreach (var frame in plan.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = frame.Offset - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }

            if (previousBounds.HasValue && previousBounds.Value == frame.Bounds)
            {
                continue;
            }

            previousBounds = frame.Bounds;
            NativeMethods.MoveWindow(windowHandle, frame.Bounds.X, frame.Bounds.Y, frame.Bounds.Width, frame.Bounds.Height, true);
        }

        if (!previousBounds.HasValue || previousBounds.Value != plan.FinalBounds)
        {
            NativeMethods.MoveWindow(windowHandle, plan.FinalBounds.X, plan.FinalBounds.Y, plan.FinalBounds.Width, plan.FinalBounds.Height, true);
        }
    }
}
