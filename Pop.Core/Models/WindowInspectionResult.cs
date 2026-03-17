using System.Drawing;

namespace Pop.Core.Models;

public sealed record WindowInspectionResult(
    IntPtr WindowHandle,
    Rectangle Bounds,
    MonitorInfo MonitorInfo,
    WindowTraits Traits,
    WindowEligibilityResult Eligibility);
