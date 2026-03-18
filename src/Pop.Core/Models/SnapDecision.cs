using System.Drawing;

namespace Pop.Core.Models;

public sealed record SnapDecision(
    SnapTarget Target,
    MonitorInfo TargetMonitorInfo,
    Point ProjectedLandingPoint,
    double HorizontalVelocityPxPerSec,
    double VerticalVelocityPxPerSec,
    double HorizontalDominanceRatio,
    bool IsQualified,
    SnapRejectionReason RejectionReason)
{
    public static SnapDecision None(
        SnapRejectionReason rejectionReason,
        MonitorInfo targetMonitorInfo,
        Point projectedLandingPoint,
        double horizontalVelocityPxPerSec = 0,
        double verticalVelocityPxPerSec = 0,
        double horizontalDominanceRatio = 0) =>
        new(
            SnapTarget.None,
            targetMonitorInfo,
            projectedLandingPoint,
            horizontalVelocityPxPerSec,
            verticalVelocityPxPerSec,
            horizontalDominanceRatio,
            false,
            rejectionReason);
}
