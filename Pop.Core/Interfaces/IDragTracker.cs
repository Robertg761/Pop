using Pop.Core.Events;

namespace Pop.Core.Interfaces;

public interface IDragTracker : IDisposable
{
    event EventHandler<DragSessionEventArgs>? DragStarted;

    event EventHandler<DragSessionEventArgs>? DragUpdated;

    event EventHandler<DragSessionCompletedEventArgs>? DragCompleted;

    void Start();

    void Stop();
}
