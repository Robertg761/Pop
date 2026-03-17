namespace Pop.Core.Models;

public enum WindowEligibilityReason
{
    Supported = 0,
    NotTitleBar,
    NotVisible,
    NotResizable,
    Minimized,
    Maximized,
    NotTopLevelDesktopWindow,
    Fullscreen,
    ElevatedProcess,
    Cloaked,
    CurrentProcessWindow,
    Unknown
}
