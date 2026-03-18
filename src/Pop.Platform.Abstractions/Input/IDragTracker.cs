using Pop.Core.Events;

namespace Pop.Platform.Abstractions.Input;

public interface IDragTracker : IDisposable
{
    event EventHandler<DragSessionRejectedEventArgs>? DragRejected;

    event EventHandler<DragSessionEventArgs>? DragStarted;

    event EventHandler<DragSessionEventArgs>? DragUpdated;

    event EventHandler<DragSessionCompletedEventArgs>? DragCompleted;

    void Start();

    void Stop();
}
