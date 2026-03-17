using Pop.Core.Interfaces;
using Pop.Core.Models;

namespace Pop.Core.Services;

public sealed class SnapDecider : ISnapDecider
{
    private static readonly TimeSpan VelocityWindow = TimeSpan.FromMilliseconds(120);

    public SnapDecision Decide(DragSession session, AppSettings settings)
    {
        if (session.Samples.Count < 2)
        {
            return SnapDecision.None();
        }

        var lastSample = session.Samples[^1];
        var cutoff = lastSample.Timestamp - VelocityWindow;
        var relevantSamples = session.Samples.Where(sample => sample.Timestamp >= cutoff).ToArray();

        if (relevantSamples.Length < 2)
        {
            relevantSamples = session.Samples.Skip(Math.Max(0, session.Samples.Count - 4)).ToArray();
        }

        if (relevantSamples.Length < 2)
        {
            return SnapDecision.None();
        }

        var firstSample = relevantSamples[0];
        var elapsedSeconds = (lastSample.Timestamp - firstSample.Timestamp).TotalSeconds;

        if (elapsedSeconds <= 0)
        {
            return SnapDecision.None();
        }

        var horizontalVelocity = (lastSample.Position.X - firstSample.Position.X) / elapsedSeconds;
        var verticalVelocity = (lastSample.Position.Y - firstSample.Position.Y) / elapsedSeconds;
        var absHorizontal = Math.Abs(horizontalVelocity);
        var absVertical = Math.Abs(verticalVelocity);
        var dominanceRatio = absVertical < 1 ? absHorizontal : absHorizontal / absVertical;

        if (absHorizontal < settings.ThrowVelocityThresholdPxPerSec)
        {
            return SnapDecision.None(horizontalVelocity, verticalVelocity, dominanceRatio);
        }

        if (dominanceRatio < settings.HorizontalDominanceRatio)
        {
            return SnapDecision.None(horizontalVelocity, verticalVelocity, dominanceRatio);
        }

        var target = horizontalVelocity < 0 ? SnapTarget.LeftHalf : SnapTarget.RightHalf;
        return new SnapDecision(target, horizontalVelocity, verticalVelocity, dominanceRatio, true);
    }
}
