using Velopack;
using Velopack.Sources;

namespace Pop.App.Services;

internal sealed class VelopackUpdateClient : IUpdateClient
{
    private readonly UpdateManager _updateManager;

    public VelopackUpdateClient()
        : this(new UpdateManager(new GithubSource(AppReleaseMetadata.RepositoryUrl, string.Empty, false), null, null))
    {
    }

    internal VelopackUpdateClient(UpdateManager updateManager)
    {
        _updateManager = updateManager;
    }

    public string CurrentVersion => AppReleaseMetadata.NormalizeVersion(_updateManager.CurrentVersion?.ToString()) ?? AppReleaseMetadata.CurrentVersion;

    public bool IsSupported => _updateManager.IsInstalled && !_updateManager.IsPortable;

    public string UnsupportedReason => "Install Pop from an official GitHub release to enable in-app updates.";

    public string? PendingRestartVersion => AppReleaseMetadata.NormalizeVersion(_updateManager.UpdatePendingRestart?.Version?.ToString());

    public async Task<UpdateDownloadResult> CheckForUpdatesAndDownloadAsync(Action<UpdateDownloadProgress> progress, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return new UpdateDownloadResult(UpdateDownloadOutcome.Unsupported, UnsupportedReason);
        }

        if (_updateManager.UpdatePendingRestart is { } pendingRelease)
        {
            var pendingVersion = AppReleaseMetadata.NormalizeVersion(pendingRelease.Version?.ToString());
            return new UpdateDownloadResult(
                UpdateDownloadOutcome.ReadyToInstall,
                CreateReadyMessage(pendingVersion),
                pendingVersion);
        }

        var updateInfo = await _updateManager.CheckForUpdatesAsync();
        if (updateInfo is null)
        {
            return new UpdateDownloadResult(UpdateDownloadOutcome.NoUpdate, "You're up to date.");
        }

        var targetVersion = AppReleaseMetadata.NormalizeVersion(updateInfo.TargetFullRelease.Version?.ToString());
        await _updateManager.DownloadUpdatesAsync(
            updateInfo,
            percent => progress(new UpdateDownloadProgress(targetVersion, percent)),
            cancellationToken);

        var preparedRelease = _updateManager.UpdatePendingRestart;
        var preparedVersion = AppReleaseMetadata.NormalizeVersion(preparedRelease?.Version?.ToString()) ?? targetVersion;

        return new UpdateDownloadResult(
            UpdateDownloadOutcome.ReadyToInstall,
            CreateReadyMessage(preparedVersion),
            preparedVersion);
    }

    public bool PreparePendingUpdateAndRestart()
    {
        if (!IsSupported)
        {
            return false;
        }

        var pendingRelease = _updateManager.UpdatePendingRestart;
        if (pendingRelease is null)
        {
            return false;
        }

        _updateManager.WaitExitThenApplyUpdates(pendingRelease, silent: false, restart: true);
        return true;
    }

    private static string CreateReadyMessage(string? version)
    {
        return string.IsNullOrWhiteSpace(version)
            ? "An update is ready to install."
            : $"Update v{version} is ready to install.";
    }
}
