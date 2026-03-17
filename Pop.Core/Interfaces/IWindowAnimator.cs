using System.Drawing;

namespace Pop.Core.Interfaces;

public interface IWindowAnimator
{
    Task AnimateToTileAsync(IntPtr windowHandle, Rectangle targetBounds, double releaseVelocityX, int durationMs, CancellationToken cancellationToken = default);
}
