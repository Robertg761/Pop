using System.Drawing;

namespace Pop.Platform.Abstractions.Windowing;

public interface IWindowSnapBoundsCalculator
{
    Rectangle GetSnapBounds(IntPtr windowHandle, Rectangle visibleTargetBounds);
}
