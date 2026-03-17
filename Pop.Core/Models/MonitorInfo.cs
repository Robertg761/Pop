using System.Drawing;

namespace Pop.Core.Models;

public readonly record struct MonitorInfo(Rectangle Bounds, Rectangle WorkArea)
{
    public static MonitorInfo Empty { get; } = new(Rectangle.Empty, Rectangle.Empty);
}
