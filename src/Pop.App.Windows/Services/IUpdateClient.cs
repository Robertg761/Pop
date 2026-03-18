namespace Pop.App.Windows.Services;

internal interface IUpdateClient
{
    string CurrentVersion { get; }

    bool IsSupported { get; }

    string UnsupportedReason { get; }

    string? PendingRestartVersion { get; }

    Task<UpdateDownloadResult> CheckForUpdatesAndDownloadAsync(Action<UpdateDownloadProgress> progress, CancellationToken cancellationToken);

    bool PreparePendingUpdateAndRestart();
}
