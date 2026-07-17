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
            MoveWindowAsyncSafe(windowHandle, plan.FinalBounds);
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
            MoveWindowAsyncSafe(windowHandle, frame.Bounds);
        }

        if (!previousBounds.HasValue || previousBounds.Value != plan.FinalBounds)
        {
            MoveWindowAsyncSafe(windowHandle, plan.FinalBounds);
        }
    }

    // Reposition via SetWindowPos with SWP_ASYNCWINDOWPOS so the call never blocks on the
    // target window's message loop. The original MoveWindow sent WM_WINDOWPOSCHANGING
    // synchronously, which could freeze the caller — including the low-level mouse hook thread
    // during an in-drag restore — if the target application was hung.
    private static void MoveWindowAsyncSafe(IntPtr windowHandle, Rectangle bounds)
    {
        NativeMethods.SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpAsyncWindowPos);
    }
}
