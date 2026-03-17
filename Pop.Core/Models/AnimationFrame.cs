using System.Drawing;

namespace Pop.Core.Models;

public readonly record struct AnimationFrame(TimeSpan Offset, Rectangle Bounds);
