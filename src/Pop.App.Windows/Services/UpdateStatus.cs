namespace Pop.App.Windows.Services;

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
