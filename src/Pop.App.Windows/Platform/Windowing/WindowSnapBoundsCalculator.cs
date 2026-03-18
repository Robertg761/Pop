using System.Drawing;
using Pop.App.Windows.Platform.Interop;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Windows.Platform.Windowing;

public sealed class WindowSnapBoundsCalculator : IWindowSnapBoundsCalculator
{
    public Rectangle GetSnapBounds(IntPtr windowHandle, Rectangle visibleTargetBounds)
    {
        if (windowHandle == IntPtr.Zero || visibleTargetBounds == Rectangle.Empty)
        {
            return visibleTargetBounds;
        }

        if (!NativeMethods.GetWindowRect(windowHandle, out var windowRect))
        {
            return visibleTargetBounds;
        }

        if (NativeMethods.DwmGetWindowAttribute(
                windowHandle,
                NativeMethods.DwmwaExtendedFrameBounds,
                out NativeMethods.RectStruct extendedFrameRect,
                System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RectStruct>()) != 0)
        {
            return visibleTargetBounds;
        }

        return AdjustWindowBoundsForVisibleTarget(
            visibleTargetBounds,
            windowRect.ToRectangle(),
            extendedFrameRect.ToRectangle());
    }

    public static Rectangle AdjustWindowBoundsForVisibleTarget(
        Rectangle visibleTargetBounds,
        Rectangle windowBounds,
        Rectangle visibleWindowBounds)
    {
        if (visibleTargetBounds == Rectangle.Empty ||
            windowBounds == Rectangle.Empty ||
            visibleWindowBounds == Rectangle.Empty)
        {
            return visibleTargetBounds;
        }

        var leftInset = visibleWindowBounds.Left - windowBounds.Left;
        var topInset = visibleWindowBounds.Top - windowBounds.Top;
        var rightInset = windowBounds.Right - visibleWindowBounds.Right;
        var bottomInset = windowBounds.Bottom - visibleWindowBounds.Bottom;

        if (leftInset < 0 || topInset < 0 || rightInset < 0 || bottomInset < 0)
        {
            return visibleTargetBounds;
        }

        return Rectangle.FromLTRB(
            visibleTargetBounds.Left - leftInset,
            visibleTargetBounds.Top - topInset,
            visibleTargetBounds.Right + rightInset,
            visibleTargetBounds.Bottom + bottomInset);
    }
}
