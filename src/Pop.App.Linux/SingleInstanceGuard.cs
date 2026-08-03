using Pop.App.Linux.Services;

namespace Pop.App.Linux;

/// <summary>
/// Holds an exclusive advisory lock on a runtime file so only one Pop instance runs per user
/// session. Two instances would run duplicate drag pollers and race each other loading and
/// unloading the KWin script. The OS releases the lock automatically if the process dies.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly FileStream? _lockStream;

    private SingleInstanceGuard(FileStream? lockStream)
    {
        _lockStream = lockStream;
    }

    /// <summary>
    /// Returns a guard on success, or <c>null</c> when another Pop instance already holds the
    /// lock. When the lock file cannot be created at all (e.g. an unwritable directory) the app
    /// runs unguarded rather than refusing to start.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var directory = !string.IsNullOrWhiteSpace(runtimeDirectory) && Directory.Exists(runtimeDirectory)
            ? runtimeDirectory
            : LinuxPaths.ConfigDirectory;

        try
        {
            Directory.CreateDirectory(directory);
            // FileShare.None maps to an exclusive lock on Unix; a second open throws IOException.
            var lockStream = new FileStream(
                Path.Combine(directory, "pop.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return new SingleInstanceGuard(lockStream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return new SingleInstanceGuard(null);
        }
    }

    public void Dispose()
    {
        _lockStream?.Dispose();
    }
}
