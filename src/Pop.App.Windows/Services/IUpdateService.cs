namespace Pop.App.Windows.Services;

internal interface IUpdateService : IDisposable
{
    event EventHandler<UpdateStateChangedEventArgs>? StateChanged;

    UpdateState CurrentState { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task CheckNowAsync(CancellationToken cancellationToken = default);

    void ApplyPendingUpdateAndRestart();
}
