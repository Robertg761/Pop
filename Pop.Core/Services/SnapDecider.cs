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
            return SnapDecision.None(SnapRejectionReason.InsufficientSamples);
        }

        var lastSample = session.Samples[^1];
        var cutoff = lastSample.Timestamp - VelocityWindow;
        var firstIndex = FindFirstRelevantSampleIndex(session.Samples, cutoff);
        if (session.Samples.Count - firstIndex < 2)
        {
            firstIndex = Math.Max(0, session.Samples.Count - 4);
        }

        if (session.Samples.Count - firstIndex < 2)
        {
            return SnapDecision.None(SnapRejectionReason.InsufficientSamples);
        }

        var firstSample = session.Samples[firstIndex];
        var elapsedSeconds = (lastSample.Timestamp - firstSample.Timestamp).TotalSeconds;

        if (elapsedSeconds <= 0)
        {
            return SnapDecision.None(SnapRejectionReason.InvalidSampleWindow);
        }

        var horizontalVelocity = (lastSample.Position.X - firstSample.Position.X) / elapsedSeconds;
        var verticalVelocity = (lastSample.Position.Y - firstSample.Position.Y) / elapsedSeconds;
        var absHorizontal = Math.Abs(horizontalVelocity);
        var absVertical = Math.Abs(verticalVelocity);
        var dominanceRatio = absVertical < 1 ? absHorizontal : absHorizontal / absVertical;

        if (absHorizontal < settings.ThrowVelocityThresholdPxPerSec)
        {
            return SnapDecision.None(SnapRejectionReason.InsufficientVelocity, horizontalVelocity, verticalVelocity, dominanceRatio);
        }

        if (dominanceRatio < settings.HorizontalDominanceRatio)
        {
            return SnapDecision.None(SnapRejectionReason.InsufficientHorizontalDominance, horizontalVelocity, verticalVelocity, dominanceRatio);
        }

        var target = horizontalVelocity < 0 ? SnapTarget.LeftHalf : SnapTarget.RightHalf;
        return new SnapDecision(target, horizontalVelocity, verticalVelocity, dominanceRatio, true, SnapRejectionReason.None);
    }

    private static int FindFirstRelevantSampleIndex(IReadOnlyList<DragSample> samples, DateTimeOffset cutoff)
    {
        for (var index = samples.Count - 1; index >= 0; index--)
        {
            if (samples[index].Timestamp < cutoff)
            {
                return Math.Min(samples.Count - 1, index + 1);
            }
        }

        return 0;
    }
}
