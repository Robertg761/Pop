using System.Drawing;

namespace Pop.Core.Models;

public readonly record struct OverlayTransition(OverlayTransitionAction Action, SnapTarget Target, Rectangle Bounds);
