using System.Drawing;
using Pop.Core.Models;

namespace Pop.Core.Events;

public sealed class DragSessionRejectedEventArgs(Point screenPoint, WindowInspectionResult inspectionResult) : EventArgs
{
    public Point ScreenPoint { get; } = screenPoint;

    public WindowInspectionResult InspectionResult { get; } = inspectionResult;
}
