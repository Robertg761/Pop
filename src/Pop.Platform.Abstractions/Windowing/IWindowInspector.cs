using System.Drawing;
using Pop.Core.Models;

namespace Pop.Platform.Abstractions.Windowing;

public interface IWindowInspector
{
    WindowInspectionResult InspectWindowAt(Point screenPoint);

    MonitorInfo InspectMonitorAt(Point screenPoint);

    WindowStateSnapshot InspectWindowState(IntPtr windowHandle);
}
