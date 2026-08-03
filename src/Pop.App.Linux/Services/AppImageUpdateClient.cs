namespace Pop.App.Linux.Services;

internal sealed class AppImageUpdateClient : IUpdateClient
{
    private readonly GitHubReleaseClient _releaseClient;
    private readonly PreparedAppImageUpdateInstaller _installer;

    public AppImageUpdateClient(
        GitHubReleaseClient? releaseClient = null,
        PreparedAppImageUpdateInstaller? installer = null)
    {
        _releaseClient = releaseClient ?? new GitHubReleaseClient();
        _installer = installer ?? new PreparedAppImageUpdateInstaller();
    }

    public string CurrentVersion => AppReleaseMetadata.CurrentVersion;

    public bool IsSupported => _installer.IsSupportedInstallation;

    public string UnsupportedReason => _installer.UnsupportedReason;

    public string? PendingRestartVersion
    {
        get
        {
            try
            {
                return _installer.LoadPreparedUpdate()?.Version;
            }
            catch
            {
                return null;
            }
        }
    }

    public async Task<UpdateDownloadResult> CheckForUpdatesAndDownloadAsync(Action<UpdateDownloadProgress> progress, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return new UpdateDownloadResult(UpdateDownloadOutcome.Unsupported, UnsupportedReason);
        }

        _installer.RemoveObsoletePreparedUpdate(CurrentVersion);
        if (_installer.LoadPreparedUpdate() is { } pendingUpdate)
        {
            return new UpdateDownloadResult(
                UpdateDownloadOutcome.ReadyToInstall,
                CreateReadyMessage(pendingUpdate.Version),
                pendingUpdate.Version);
        }

        _installer.CleanUpAbandonedDownloads();

        var release = await _releaseClient.FetchLatestLinuxReleaseAsync(cancellationToken);
        if (release is null)
        {
            // The AppImage asset is uploaded by a separate workflow shortly after a release;
            // until it exists there is nothing to update to.
            return new UpdateDownloadResult(UpdateDownloadOutcome.NoUpdate, "Pop is up to date.");
        }

        if (!AppVersion.TryParse(release.Version, out var latestVersion)
            || !AppVersion.TryParse(CurrentVersion, out var installedVersion))
        {
            return new UpdateDownloadResult(
                UpdateDownloadOutcome.Error,
                "Update check failed: couldn't compare app versions.");
        }

        if (latestVersion <= installedVersion)
        {
            return new UpdateDownloadResult(UpdateDownloadOutcome.NoUpdate, "Pop is up to date.");
        }

        var downloadPath = _installer.CreateDownloadPath(release.Version);
        progress(new UpdateDownloadProgress(release.Version, 0));
        await _releaseClient.DownloadReleaseAsync(
            release,
            downloadPath,
            percentage => progress(new UpdateDownloadProgress(release.Version, percentage)),
            cancellationToken);

        var preparedUpdate = _installer.PrepareUpdate(downloadPath, release.Version);
        return new UpdateDownloadResult(
            UpdateDownloadOutcome.ReadyToInstall,
            CreateReadyMessage(preparedUpdate.Version),
            preparedUpdate.Version);
    }

    public bool PreparePendingUpdateAndRestart()
    {
        if (!IsSupported)
        {
            return false;
        }

        var pendingUpdate = _installer.LoadPreparedUpdate();
        if (pendingUpdate is null)
        {
            return false;
        }

        try
        {
            _installer.InstallPreparedUpdate(pendingUpdate);
            return true;
        }
        catch
        {
            // A missing staged file or a failed installer launch must not crash the app;
            // the service reports the failure so the user can re-check for updates.
            return false;
        }
    }

    private static string CreateReadyMessage(string? version)
    {
        return string.IsNullOrWhiteSpace(version)
            ? "An update is ready to install."
            : $"Update v{version} is ready to install.";
    }
}
