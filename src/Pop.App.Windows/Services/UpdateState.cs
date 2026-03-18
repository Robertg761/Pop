namespace Pop.App.Windows.Services;

internal sealed record UpdateState(
    UpdateStatus Status,
    string CurrentVersion,
    string Message,
    string? AvailableVersion = null,
    int? DownloadProgressPercent = null,
    bool CanCheck = true,
    bool CanInstall = false);
