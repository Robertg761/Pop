using System.Drawing;

namespace Pop.Core.Models;

public readonly record struct SnapRestoreState(Rectangle RestoreBounds, Rectangle SnappedBounds);
