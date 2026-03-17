namespace Pop.App.Services;

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

    private Task? _backgroundTask;
    private bool _started;

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
            _backgroundTask = Task.Run(() => BackgroundLoopAsync(_disposeCancellation.Token), _disposeCancellation.Token);
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
            PublishState(CreateInitialState());
            return;
        }

        _shutdownHandler.RequestShutdown();
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();
        _checkGate.Dispose();
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
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
        var effectiveCancellation = linkedCancellation.Token;

        await _checkGate.WaitAsync(effectiveCancellation);
        try
        {
            PublishState(CreateCheckingState());

            UpdateDownloadResult result;
            try
            {
                result = await _updateClient.CheckForUpdatesAndDownloadAsync(OnDownloadProgress, effectiveCancellation);
            }
            catch (OperationCanceledException) when (effectiveCancellation.IsCancellationRequested)
            {
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
            _checkGate.Release();
        }
    }

    private void OnDownloadProgress(UpdateDownloadProgress progress)
    {
        PublishState(new UpdateState(
            UpdateStatus.Downloading,
            _updateClient.CurrentVersion,
            CreateDownloadMessage(progress.TargetVersion, progress.Percentage),
            progress.TargetVersion,
            Math.Clamp(progress.Percentage, 0, 100),
            CanCheck: false,
            CanInstall: false));
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
            "Ready to check GitHub for updates.",
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
            "Checking GitHub for updates...",
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

    private void PublishState(UpdateState state)
    {
        if (Equals(CurrentState, state))
        {
            return;
        }

        CurrentState = state;
        StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(state));
    }
}
