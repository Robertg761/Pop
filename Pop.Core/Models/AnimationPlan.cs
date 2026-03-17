using System.Collections.ObjectModel;
using System.Drawing;

namespace Pop.Core.Models;

public sealed class AnimationPlan
{
    public AnimationPlan(IReadOnlyList<AnimationFrame> frames, Rectangle finalBounds, int durationMs, int maxOvershootPx)
    {
        Frames = new ReadOnlyCollection<AnimationFrame>(frames.ToList());
        FinalBounds = finalBounds;
        DurationMs = durationMs;
        MaxOvershootPx = maxOvershootPx;
    }

    public ReadOnlyCollection<AnimationFrame> Frames { get; }

    public Rectangle FinalBounds { get; }

    public int DurationMs { get; }

    public int MaxOvershootPx { get; }
}
