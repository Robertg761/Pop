using System.Drawing;
using Pop.Core.Models;

namespace Pop.Core.Services;

public static class TileLayoutCalculator
{
    public static Rectangle GetTileBounds(SnapTarget target, MonitorInfo monitorInfo)
    {
        if (target is SnapTarget.None)
        {
            return Rectangle.Empty;
        }

        var workArea = monitorInfo.WorkArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            // A degenerate or negative-size work area (e.g. a monitor mid-reconfiguration)
            // would otherwise produce a zero-width snap plan downstream.
            return Rectangle.Empty;
        }

        var leftWidth = workArea.Width / 2;

        return target switch
        {
            SnapTarget.LeftHalf => new Rectangle(workArea.X, workArea.Y, leftWidth, workArea.Height),
            SnapTarget.RightHalf => new Rectangle(workArea.X + leftWidth, workArea.Y, workArea.Width - leftWidth, workArea.Height),
            _ => Rectangle.Empty
        };
    }
}
