using System.Diagnostics;

namespace Pop.App.Linux.Platform;

/// <summary>
/// Detects whether a StatusNotifierItem host (a "system tray") is present on the session bus.
/// Pop is tray-first; on desktops without a tray host (e.g. stock GNOME) the app would be
/// invisible and uncontrollable, so callers show the settings window instead.
/// </summary>
internal static class StatusNotifierHostDetector
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);
    private static readonly string[] WatcherNames =
    [
        "org.kde.StatusNotifierWatcher",
        "org.freedesktop.StatusNotifierWatcher"
    ];

    public static async Task<bool> IsHostAvailableAsync(CancellationToken cancellationToken = default)
    {
        foreach (var watcherName in WatcherNames)
        {
            if (await QueryNameHasOwnerAsync(watcherName, cancellationToken) == true)
            {
                return true;
            }
        }

        // Either no watcher owns the bus name, or neither query tool is installed. Treat both
        // as "no tray host" so the app is never unreachable.
        return false;
    }

    private static async Task<bool?> QueryNameHasOwnerAsync(string busName, CancellationToken cancellationToken)
    {
        var gdbusOutput = await RunAsync(
            "gdbus",
            [
                "call", "--session",
                "--dest", "org.freedesktop.DBus",
                "--object-path", "/org/freedesktop/DBus",
                "--method", "org.freedesktop.DBus.NameHasOwner",
                busName
            ],
            cancellationToken);
        if (gdbusOutput is not null)
        {
            return gdbusOutput.Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        var dbusSendOutput = await RunAsync(
            "dbus-send",
            [
                "--session", "--print-reply",
                "--dest=org.freedesktop.DBus",
                "/org/freedesktop/DBus",
                "org.freedesktop.DBus.NameHasOwner",
                $"string:{busName}"
            ],
            cancellationToken);
        if (dbusSendOutput is not null)
        {
            return dbusSendOutput.Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static async Task<string?> RunAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(QueryTimeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var startInfo = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(linkedCancellation.Token);
            try
            {
                await process.WaitForExitAsync(linkedCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return null;
            }

            return process.ExitCode == 0 ? await standardOutput : null;
        }
        catch
        {
            return null;
        }
    }
}
