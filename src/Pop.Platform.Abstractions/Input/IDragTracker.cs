using Pop.Core.Events;

namespace Pop.Platform.Abstractions.Input;

/// <summary>
/// Observes title-bar drag gestures. Events fire synchronously on the implementation's input
/// thread (a platform hook, event tap, or polling loop) and the <c>DragSession</c> they carry is
/// not thread-safe — handlers must not retain it or read its samples from another thread while a
/// drag is in progress. No events fire after <see cref="Stop"/> returns or after disposal.
/// </summary>
public interface IDragTracker : IDisposable
{
    event EventHandler<DragSessionRejectedEventArgs>? DragRejected;

    event EventHandler<DragSessionEventArgs>? DragStarted;

    event EventHandler<DragSessionEventArgs>? DragUpdated;

    event EventHandler<DragSessionCompletedEventArgs>? DragCompleted;

    void Start();

    void Stop();
}
