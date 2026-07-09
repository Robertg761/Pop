using System.Drawing;

namespace Pop.Core.Models;

public readonly record struct QualifiedSnapPlan(
    SnapDecision Decision,
    MonitorInfo ActiveMonitor,
    Rectangle VisibleTileBounds,
    Rectangle SnapBounds,
    AnimationPlan AnimationPlan);
