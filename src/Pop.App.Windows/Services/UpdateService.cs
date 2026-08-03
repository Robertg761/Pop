namespace Pop.App.Windows.Services;

internal sealed class UpdateService : IUpdateService
{
    private static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(6);

    private readonly IUpdateClient _updateClient;
    private readonly IAppShutdownHandler _shutdownHandler;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _checkInterval;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _activeCheckSync = new();
    private readonly object _stateSync = new();
    private int _activeCheckCount;
    private TaskCompletionSource<bool> _allChecksCompleted = CreateCompletedCheckSignal();

    private Task? _backgroundTask;
    private CancellationTokenSource? _backgroundCancellation;
    private bool _started;
    private volatile bool _disposed;

    public UpdateService(
        IUpdateClient? updateClient = null,
        IAppShutdownHandler? shutdownHandler = null,
        TimeSpan? initialDelay = null,
        TimeSpan? checkInterval = null)
    {
        _updateClient = updateClient ?? new VelopackUpdateClient();
        _shutdownHandler = shutdownHandler ?? new WpfAppShutdownHandler();
        _initialDelay = initialDelay ?? DefaultInitialDelay;
        _checkInterval = checkInterval ?? DefaultCheckInterval;

        CurrentState = CreateInitialState();
    }

    public event EventHandler<UpdateStateChangedEventArgs>? StateChanged;

    public UpdateState CurrentState { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;
        PublishState(CreateInitialState());

        if (_updateClient.IsSupported)
        {
            _backgroundCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
            _backgroundTask = Task.Run(() => BackgroundLoopAsync(_backgroundCancellation.Token), _backgroundCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesInternalAsync(cancellationToken);
    }

    public void ApplyPendingUpdateAndRestart()
    {
        if (!_updateClient.PreparePendingUpdateAndRestart())
        {
            PublishState(CreatePendingUpdateUnavailableState());
            return;
        }

        _shutdownHandler.RequestShutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _backgroundCancellation?.Cancel();
        _disposeCancellation.Cancel();

        // Let the background loop (and any in-flight check it is running) observe cancellation
        // and unwind before disposing the primitives it uses, so we don't race it into an
        // ObjectDisposedException.
        var checksCompleted = GetChecksCompletedTask();
        var checksStopped = TryWaitForCompletion(checksCompleted);

        if (_backgroundTask is not null)
        {
            TryWaitForCompletion(_backgroundTask);
        }

        if (checksStopped)
        {
            _checkGate.Dispose();
        }

        _backgroundCancellation?.Dispose();
        _disposeCancellation.Dispose();
    }

    private async Task BackgroundLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_initialDelay, cancellationToken);
            await CheckForUpdatesInternalAsync(cancellationToken);

            using var timer = new PeriodicTimer(_checkInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await CheckForUpdatesInternalAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CheckForUpdatesInternalAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        BeginTrackedCheck();
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
            var effectiveCancellation = linkedCancellation.Token;

            await _checkGate.WaitAsync(effectiveCancellation);

            try
            {
                var previousState = CurrentState;
                PublishState(CreateCheckingState());

                UpdateDownloadResult result;
                try
                {
                    result = await _updateClient.CheckForUpdatesAndDownloadAsync(OnDownloadProgress, effectiveCancellation);
                }
                catch (OperationCanceledException) when (effectiveCancellation.IsCancellationRequested)
                {
                    // Restore a terminal state so the Check button isn't left disabled.
                    PublishState(CreateStateAfterCancelledCheck(previousState));
                    return;
                }
                catch (Exception exception)
                {
                    result = new UpdateDownloadResult(
                        UpdateDownloadOutcome.Error,
                        $"Update check failed: {exception.Message}");
                }

                PublishState(result.Outcome switch
                {
                    UpdateDownloadOutcome.NoUpdate => new UpdateState(
                        UpdateStatus.UpToDate,
                        _updateClient.CurrentVersion,
                        result.Message,
                        CanCheck: _updateClient.IsSupported,
                        CanInstall: false),
                    UpdateDownloadOutcome.ReadyToInstall => CreateReadyToInstallState(result.TargetVersion, result.Message),
                    UpdateDownloadOutcome.Unsupported => CreateUnsupportedState(),
                    UpdateDownloadOutcome.Error => new UpdateState(
                        UpdateStatus.Error,
                        _updateClient.CurrentVersion,
                        result.Message,
                        CanCheck: _updateClient.IsSupported,
                        CanInstall: false),
                    _ => CreateInitialState()
                });
            }
            finally
            {
                ReleaseCheckGate();
            }
        }
        finally
        {
            EndTrackedCheck();
        }
    }

    private void OnDownloadProgress(UpdateDownloadProgress progress)
    {
        PublishStateCore(
            new UpdateState(
                UpdateStatus.Downloading,
                _updateClient.CurrentVersion,
                CreateDownloadMessage(progress.TargetVersion, progress.Percentage),
                progress.TargetVersion,
                Math.Clamp(progress.Percentage, 0, 100),
                CanCheck: false,
                CanInstall: false),
            progressOnly: true);
    }

    private UpdateState CreateInitialState()
    {
        if (!_updateClient.IsSupported)
        {
            return CreateUnsupportedState();
        }

        if (!string.IsNullOrWhiteSpace(_updateClient.PendingRestartVersion))
        {
            return CreateReadyToInstallState(
                _updateClient.PendingRestartVersion,
                CreateReadyMessage(_updateClient.PendingRestartVersion));
        }

        return new UpdateState(
            UpdateStatus.Idle,
            _updateClient.CurrentVersion,
            "Ready to check for updates.",
            CanCheck: true,
            CanInstall: false);
    }

    private UpdateState CreateCheckingState()
    {
        if (!_updateClient.IsSupported)
        {
            return CreateUnsupportedState();
        }

        if (!string.IsNullOrWhiteSpace(_updateClient.PendingRestartVersion))
        {
            return CreateReadyToInstallState(
                _updateClient.PendingRestartVersion,
                CreateReadyMessage(_updateClient.PendingRestartVersion));
        }

        return new UpdateState(
            UpdateStatus.Checking,
            _updateClient.CurrentVersion,
            "Checking for updates...",
            CanCheck: false,
            CanInstall: false);
    }

    private UpdateState CreateUnsupportedState()
    {
        return new UpdateState(
            UpdateStatus.Unsupported,
            _updateClient.CurrentVersion,
            _updateClient.UnsupportedReason,
            CanCheck: false,
            CanInstall: false);
    }

    private UpdateState CreateReadyToInstallState(string? version, string message)
    {
        return new UpdateState(
            UpdateStatus.ReadyToInstall,
            _updateClient.CurrentVersion,
            message,
            version,
            CanCheck: true,
            CanInstall: true);
    }

    private UpdateState CreateStateAfterCancelledCheck(UpdateState previousState)
    {
        return previousState.Status is UpdateStatus.Checking or UpdateStatus.Downloading
            ? CreateInitialState()
            : previousState;
    }

    private UpdateState CreatePendingUpdateUnavailableState()
    {
        if (!_updateClient.IsSupported)
        {
            return CreateUnsupportedState();
        }

        return new UpdateState(
            UpdateStatus.Error,
            _updateClient.CurrentVersion,
            "The downloaded update is no longer available. Check for updates to download it again.",
            CanCheck: true,
            CanInstall: false);
    }

    private static string CreateDownloadMessage(string? version, int percentage)
    {
        var clampedPercentage = Math.Clamp(percentage, 0, 100);
        return string.IsNullOrWhiteSpace(version)
            ? $"Downloading update... {clampedPercentage}%"
            : $"Downloading v{version}... {clampedPercentage}%";
    }

    private static string CreateReadyMessage(string? version)
    {
        return string.IsNullOrWhiteSpace(version)
            ? "An update is ready to install."
            : $"Update v{version} is ready to install.";
    }

    private void PublishState(UpdateState state) => PublishStateCore(state, progressOnly: false);

    private void PublishStateCore(UpdateState state, bool progressOnly)
    {
        // Velopack progress callbacks arrive on download threads; serialize publishes so a
        // late progress update can't overwrite the final state of the operation.
        lock (_stateSync)
        {
            if (progressOnly && CurrentState.Status is not (UpdateStatus.Checking or UpdateStatus.Downloading))
            {
                return;
            }

            if (Equals(CurrentState, state))
            {
                return;
            }

            CurrentState = state;
            StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(state));
        }
    }

    private void BeginTrackedCheck()
    {
        lock (_activeCheckSync)
        {
            if (_activeCheckCount == 0)
            {
                _allChecksCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _activeCheckCount++;
        }
    }

    private void EndTrackedCheck()
    {
        TaskCompletionSource<bool>? completed = null;
        lock (_activeCheckSync)
        {
            if (_activeCheckCount > 0)
            {
                _activeCheckCount--;
            }

            if (_activeCheckCount == 0)
            {
                completed = _allChecksCompleted;
            }
        }

        completed?.TrySetResult(true);
    }

    private Task GetChecksCompletedTask()
    {
        lock (_activeCheckSync)
        {
            return _allChecksCompleted.Task;
        }
    }

    private static bool TryWaitForCompletion(Task task, TimeSpan? timeout = null)
    {
        try
        {
            return task.Wait(timeout ?? TimeSpan.FromSeconds(1));
        }
        catch
        {
            return true;
        }
    }

    private static TaskCompletionSource<bool> CreateCompletedCheckSignal()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult(true);
        return signal;
    }

    private void ReleaseCheckGate()
    {
        try
        {
            _checkGate.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
