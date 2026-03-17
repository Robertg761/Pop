using System.Drawing;
using Pop.Core.Models;

namespace Pop.Core.Interfaces;

public interface IWindowAnimator
{
    AnimationPlan CreatePlan(Rectangle startBounds, Rectangle targetBounds, double releaseVelocityX, int durationMs);

    Task AnimateToTileAsync(IntPtr windowHandle, AnimationPlan plan, CancellationToken cancellationToken = default);
}
