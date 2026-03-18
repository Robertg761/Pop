using Pop.Core.Models;

namespace Pop.Platform.Abstractions.Windowing;

public interface IWindowMover
{
    Task MoveWindowAsync(IntPtr windowHandle, AnimationPlan plan, CancellationToken cancellationToken = default);
}
