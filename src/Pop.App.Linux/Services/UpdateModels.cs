namespace Pop.App.Linux.Services;

internal enum UpdateStatus
{
    Unsupported,
    Idle,
    Checking,
    Downloading,
    UpToDate,
    ReadyToInstall,
    Error
}

internal enum UpdateDownloadOutcome
{
    Unsupported,
    NoUpdate,
    ReadyToInstall,
    Error
}

internal sealed record UpdateState(
    UpdateStatus Status,
    string CurrentVersion,
    string Message,
    string? AvailableVersion = null,
    int? DownloadProgressPercent = null,
    bool CanCheck = true,
    bool CanInstall = false);

internal sealed record UpdateDownloadProgress(string? TargetVersion, int Percentage);

internal sealed record UpdateDownloadResult(
    UpdateDownloadOutcome Outcome,
    string Message,
    string? TargetVersion = null);

internal sealed class UpdateStateChangedEventArgs(UpdateState state) : EventArgs
{
    public UpdateState State { get; } = state;
}

internal interface IUpdateService : IDisposable
{
    event EventHandler<UpdateStateChangedEventArgs>? StateChanged;

    UpdateState CurrentState { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task CheckNowAsync(CancellationToken cancellationToken = default);

    void ApplyPendingUpdateAndRestart();
}

internal interface IUpdateClient
{
    string CurrentVersion { get; }

    bool IsSupported { get; }

    string UnsupportedReason { get; }

    string? PendingRestartVersion { get; }

    Task<UpdateDownloadResult> CheckForUpdatesAndDownloadAsync(Action<UpdateDownloadProgress> progress, CancellationToken cancellationToken);

    bool PreparePendingUpdateAndRestart();
}

internal interface IAppShutdownHandler
{
    void RequestShutdown();
}

internal sealed class DelegateAppShutdownHandler(Action requestShutdown) : IAppShutdownHandler
{
    public void RequestShutdown() => requestShutdown();
}
