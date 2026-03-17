namespace Pop.Core.Models;

public sealed record SnapDecision(
    SnapTarget Target,
    double HorizontalVelocityPxPerSec,
    double VerticalVelocityPxPerSec,
    double HorizontalDominanceRatio,
    bool IsQualified,
    SnapRejectionReason RejectionReason)
{
    public static SnapDecision None(
        SnapRejectionReason rejectionReason,
        double horizontalVelocityPxPerSec = 0,
        double verticalVelocityPxPerSec = 0,
        double horizontalDominanceRatio = 0) =>
        new(SnapTarget.None, horizontalVelocityPxPerSec, verticalVelocityPxPerSec, horizontalDominanceRatio, false, rejectionReason);
}
