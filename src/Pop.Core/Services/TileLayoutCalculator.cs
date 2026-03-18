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
        var leftWidth = workArea.Width / 2;

        return target switch
        {
            SnapTarget.LeftHalf => new Rectangle(workArea.X, workArea.Y, leftWidth, workArea.Height),
            SnapTarget.RightHalf => new Rectangle(workArea.X + leftWidth, workArea.Y, workArea.Width - leftWidth, workArea.Height),
            _ => Rectangle.Empty
        };
    }
}
