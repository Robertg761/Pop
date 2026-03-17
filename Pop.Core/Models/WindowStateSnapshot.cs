using System.Drawing;

namespace Pop.Core.Models;

public sealed record WindowStateSnapshot(Rectangle Bounds, MonitorInfo MonitorInfo);
