namespace Pop.App.Services;

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
