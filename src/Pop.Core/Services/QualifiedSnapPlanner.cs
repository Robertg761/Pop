using System.Drawing;
using Pop.Core.Interfaces;
using Pop.Core.Models;

namespace Pop.Core.Services;

/// <summary>
/// Shared pure planning for a qualified snap: decide, resolve tile bounds, and build an animation plan.
/// Platform hosts still own window refresh timing, I/O, and move execution.
/// </summary>
public sealed class QualifiedSnapPlanner(ISnapDecider snapDecider, WindowAnimator? windowAnimator = null)
{
    private readonly ISnapDecider _snapDecider = snapDecider;
    private readonly WindowAnimator _windowAnimator = windowAnimator ?? new WindowAnimator();

    public SnapDecision Decide(DragSession session, AppSettings settings)
        => _snapDecider.Decide(session, settings);

    public bool TryCreatePlan(
        DragSession session,
        SnapDecision decision,
        AppSettings settings,
        Func<IntPtr, Rectangle, Rectangle> getSnapBounds,
        out QualifiedSnapPlan plan)
    {
        plan = default;

        if (!decision.IsQualified)
        {
            return false;
        }

        var activeMonitor = decision.TargetMonitorInfo != MonitorInfo.Empty
            ? decision.TargetMonitorInfo
            : session.CurrentMonitorInfo;

        var visibleTileBounds = TileLayoutCalculator.GetTileBounds(decision.Target, activeMonitor);
        if (visibleTileBounds == Rectangle.Empty)
        {
            return false;
        }

        var snapBounds = getSnapBounds(session.WindowHandle, visibleTileBounds);
        var animationPlan = _windowAnimator.CreatePlan(
            session.CurrentBounds,
            snapBounds,
            decision.HorizontalVelocityPxPerSec,
            settings.GlideDurationMs);

        plan = new QualifiedSnapPlan(
            decision,
            activeMonitor,
            visibleTileBounds,
            snapBounds,
            animationPlan);

        return true;
    }
}
