using System.Drawing;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Linux.Platform.X11;

public sealed class X11WindowSnapBoundsCalculator : IWindowSnapBoundsCalculator
{
    public Rectangle GetSnapBounds(IntPtr windowHandle, Rectangle visibleTargetBounds)
    {
        _ = windowHandle;
        return visibleTargetBounds;
    }
}
