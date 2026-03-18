using Pop.Core.Models;

namespace Pop.Core.Events;

public sealed class DragSessionEventArgs(DragSession session) : EventArgs
{
    public DragSession Session { get; } = session;
}
