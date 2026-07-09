using System.Drawing;
using Pop.Core.Models;

namespace Pop.Core.Services;

/// <summary>
/// Consistent diagnostic field bags for drag/snap events across platform hosts.
/// </summary>
public static class SnapDiagnosticFields
{
    public static Dictionary<string, string?> ForRejectedRelease(DragSession session, SnapDecision decision)
    {
        return new Dictionary<string, string?>
        {
            ["ctrlRelease"] = session.IsCtrlPressedAtRelease.ToString(),
            ["target"] = decision.Target.ToString(),
            ["reason"] = decision.RejectionReason.ToString(),
            ["velocityX"] = Math.Round(decision.HorizontalVelocityPxPerSec).ToString(),
            ["velocityY"] = Math.Round(decision.VerticalVelocityPxPerSec).ToString(),
            ["dominance"] = decision.HorizontalDominanceRatio.ToString("0.00"),
            ["projectedLandingPoint"] = decision.ProjectedLandingPoint.ToString(),
            ["releaseMonitor"] = session.CurrentMonitorInfo.WorkArea.ToString(),
            ["targetMonitor"] = decision.TargetMonitorInfo.WorkArea.ToString()
        };
    }

    public static Dictionary<string, string?> ForQualifiedRelease(DragSession session, QualifiedSnapPlan plan)
    {
        return new Dictionary<string, string?>
        {
            ["ctrlRelease"] = session.IsCtrlPressedAtRelease.ToString(),
            ["target"] = plan.Decision.Target.ToString(),
            ["reason"] = plan.Decision.RejectionReason.ToString(),
            ["velocityX"] = Math.Round(plan.Decision.HorizontalVelocityPxPerSec).ToString(),
            ["velocityY"] = Math.Round(plan.Decision.VerticalVelocityPxPerSec).ToString(),
            ["dominance"] = plan.Decision.HorizontalDominanceRatio.ToString("0.00"),
            ["projectedLandingPoint"] = plan.Decision.ProjectedLandingPoint.ToString(),
            ["releaseMonitor"] = session.CurrentMonitorInfo.WorkArea.ToString(),
            ["targetMonitor"] = plan.ActiveMonitor.WorkArea.ToString(),
            ["frames"] = plan.AnimationPlan.Frames.Count.ToString(),
            ["overshootPx"] = plan.AnimationPlan.MaxOvershootPx.ToString()
        };
    }

    public static Dictionary<string, string?> ForRestoreSuccess(
        IntPtr windowHandle,
        Rectangle restoreBounds,
        Rectangle snappedBounds)
    {
        return new Dictionary<string, string?>
        {
            ["windowHandle"] = windowHandle.ToString("X"),
            ["restoreBounds"] = restoreBounds.ToString(),
            ["snappedBounds"] = snappedBounds.ToString()
        };
    }

    public static Dictionary<string, string?> ForRestoreFailure(IntPtr windowHandle, Exception exception)
    {
        return new Dictionary<string, string?>
        {
            ["windowHandle"] = windowHandle.ToString("X"),
            ["error"] = exception.Message
        };
    }

    public static Dictionary<string, string?> ForUnexpectedError(IntPtr windowHandle, Exception exception)
    {
        return new Dictionary<string, string?>
        {
            ["windowHandle"] = windowHandle.ToString("X"),
            ["error"] = exception.GetType().Name,
            ["message"] = exception.Message
        };
    }
}
