using System.Diagnostics;
using System.Drawing;
using Pop.App.Windows.Platform.Interop;
using Pop.Core.Models;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Windows.Platform.Windowing;

public sealed class Win32WindowMover : IWindowMover
{
    private readonly Action<string>? _onMoveFailure;

    public Win32WindowMover(Action<string>? onMoveFailure = null)
    {
        _onMoveFailure = onMoveFailure;
    }

    public async Task MoveWindowAsync(IntPtr windowHandle, AnimationPlan plan, CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero || plan.FinalBounds == Rectangle.Empty)
        {
            return;
        }

        if (plan.Frames.Count == 0)
        {
            MoveWindowCore(windowHandle, plan.FinalBounds);
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
            MoveWindowCore(windowHandle, frame.Bounds);
        }

        if (!previousBounds.HasValue || previousBounds.Value != plan.FinalBounds)
        {
            MoveWindowCore(windowHandle, plan.FinalBounds);
        }
    }

    /// <summary>
    /// Synchronously issues a single (non-blocking, posted) move without any animation frames,
    /// for callers that must stay off async paths — e.g. the in-drag restore.
    /// </summary>
    public bool MoveWindowImmediately(IntPtr windowHandle, Rectangle bounds)
    {
        if (windowHandle == IntPtr.Zero || bounds == Rectangle.Empty)
        {
            return false;
        }

        return MoveWindowCore(windowHandle, bounds);
    }

    // Reposition via SetWindowPos with SWP_ASYNCWINDOWPOS so the call never blocks on the
    // target window's message loop. The original MoveWindow sent WM_WINDOWPOSCHANGING
    // synchronously, which could freeze the caller — including the low-level mouse hook thread
    // during an in-drag restore — if the target application was hung.
    private bool MoveWindowCore(IntPtr windowHandle, Rectangle bounds)
    {
        if (NativeMethods.SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpAsyncWindowPos))
        {
            return true;
        }

        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        _onMoveFailure?.Invoke($"SetWindowPos failed for window 0x{windowHandle:X} (Win32 error {error}).");
        return false;
    }
}
