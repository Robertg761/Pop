using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Pop.App.Linux.Services;
using Pop.Core.Models;

namespace Pop.App.Linux.Platform.KWin;

public sealed class KWinWaylandIntegration : IDisposable
{
    private const string PluginName = "pop-wayland";
    private const string ScriptResourceName = "Pop.App.Linux.Platform.KWin.pop-wayland.js";
    private static readonly TimeSpan GdbusTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ScriptWatchInterval = TimeSpan.FromSeconds(30);
    private readonly string _scriptPath = Path.Combine(LinuxPaths.ConfigDirectory, "kwin", "pop-wayland.js");
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly CancellationTokenSource _watchCancellation = new();
    private AppSettings _lastSettings = AppSettings.Default;
    private Task? _watchTask;

    public static bool IsCandidateSession()
    {
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var currentDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
        var desktopSession = Environment.GetEnvironmentVariable("DESKTOP_SESSION") ?? string.Empty;
        var isKdeSession =
            currentDesktop.Contains("KDE", StringComparison.OrdinalIgnoreCase) ||
            desktopSession.Contains("plasma", StringComparison.OrdinalIgnoreCase);

        return isKdeSession &&
               (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(waylandDisplay));
    }

    public async Task InitializeAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await WarnIfUnsupportedPlasmaAsync(cancellationToken);
        await ReloadAsync(settings, cancellationToken);

        // KWin restarts (crash, "kwin_wayland --replace") drop loaded scripts; watch for that
        // and re-register so snapping survives a KWin restart without restarting Pop.
        _watchTask = Task.Run(() => WatchScriptAsync(_watchCancellation.Token));
    }

    public async Task ReloadAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        // Serialize reloads: a settings save can race the KWin-restart watcher.
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            _lastSettings = settings;
            await WriteScriptAsync(settings, cancellationToken);

            await RunGdbusAsync(cancellationToken, allowFailure: true, "call", "--session",
                "--dest", "org.kde.KWin",
                "--object-path", "/Scripting",
                "--method", "org.kde.kwin.Scripting.unloadScript",
                PluginName);

            await RunGdbusAsync(cancellationToken, allowFailure: false, "call", "--session",
                "--dest", "org.kde.KWin",
                "--object-path", "/Scripting",
                "--method", "org.kde.kwin.Scripting.loadScript",
                _scriptPath,
                PluginName);

            await RunGdbusAsync(cancellationToken, allowFailure: false, "call", "--session",
                "--dest", "org.kde.KWin",
                "--object-path", "/Scripting",
                "--method", "org.kde.kwin.Scripting.start");
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public void Dispose()
    {
        _watchCancellation.Cancel();
        try
        {
            _watchTask?.Wait(GdbusTimeout);
        }
        catch (AggregateException)
        {
        }

        using var cancellation = new CancellationTokenSource(GdbusTimeout);
        try
        {
            RunGdbusAsync(
                cancellation.Token,
                allowFailure: true,
                "call",
                "--session",
                "--dest", "org.kde.KWin",
                "--object-path", "/Scripting",
                "--method", "org.kde.kwin.Scripting.unloadScript",
                PluginName).GetAwaiter().GetResult();
        }
        catch
        {
        }

        _watchCancellation.Dispose();
        _reloadGate.Dispose();
    }

    private async Task WatchScriptAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ScriptWatchInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (await IsScriptLoadedAsync(cancellationToken) != false)
                {
                    // Loaded, or state unknown (e.g. KWin still restarting): don't churn.
                    continue;
                }

                try
                {
                    await ReloadAsync(_lastSettings, cancellationToken);
                    Console.WriteLine("Pop re-registered its KWin script after a KWin restart.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Pop couldn't re-register its KWin script: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<bool?> IsScriptLoadedAsync(CancellationToken cancellationToken)
    {
        var output = await RunGdbusAsync(cancellationToken, allowFailure: true, "call", "--session",
            "--dest", "org.kde.KWin",
            "--object-path", "/Scripting",
            "--method", "org.kde.kwin.Scripting.isScriptLoaded",
            PluginName);
        if (output is null)
        {
            return null;
        }

        if (output.Contains("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return output.Contains("false", StringComparison.OrdinalIgnoreCase) ? false : null;
    }

    private static async Task WarnIfUnsupportedPlasmaAsync(CancellationToken cancellationToken)
    {
        // The KWin script needs the Plasma 6 scripting API; on Plasma 5.27 it loads but can
        // never work. Detection is best-effort — a missing plasmashell just skips the warning.
        var majorVersion = await DetectPlasmaMajorVersionAsync(cancellationToken);
        if (majorVersion is int version and < 6)
        {
            var message = $"Pop's Wayland snapping requires KDE Plasma 6 or newer, but Plasma {version} was detected. Snapping will stay inactive.";
            Console.Error.WriteLine(message);
            DesktopNotifier.TryNotify("Pop can't snap windows on this Plasma version", message);
        }
    }

    private static async Task<int?> DetectPlasmaMajorVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(GdbusTimeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var startInfo = new ProcessStartInfo("plasmashell")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(linkedCancellation.Token);
            await process.WaitForExitAsync(linkedCancellation.Token);

            // Output looks like "plasmashell 6.0.4".
            foreach (var token in (await standardOutput).Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var dotIndex = token.IndexOf('.');
                if (dotIndex > 0 && int.TryParse(token[..dotIndex], out var major))
                {
                    return major;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteScriptAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_scriptPath)!);

        await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException($"Unable to load embedded KWin script '{ScriptResourceName}'.");
        using var reader = new StreamReader(resource);
        var script = await reader.ReadToEndAsync(cancellationToken);
        script = script
            .Replace("__POP_ENABLED__", settings.Enabled ? "true" : "false", StringComparison.Ordinal)
            .Replace("__POP_GLIDE_DURATION_MS__", settings.GlideDurationMs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__POP_MIN_HORIZONTAL_VELOCITY__", settings.ThrowVelocityThresholdPxPerSec.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__POP_HORIZONTAL_DOMINANCE_RATIO__", settings.HorizontalDominanceRatio.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        await File.WriteAllTextAsync(_scriptPath, script, cancellationToken);
    }

    // Returns the call's standard output on success, or null when an allowed failure occurred.
    private static async Task<string?> RunGdbusAsync(
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] arguments)
    {
        using var timeout = new CancellationTokenSource(GdbusTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var startInfo = new ProcessStartInfo
        {
            FileName = "gdbus",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start gdbus.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // gdbus (from glib2 tools) is not installed. This throws even for allowFailure calls
            // because the failure is at spawn time, not exit time.
            if (allowFailure)
            {
                return null;
            }

            throw new InvalidOperationException(
                "Pop's KWin Wayland integration requires the 'gdbus' tool (part of glib2). Install it and try again.",
                exception);
        }

        using var _ = process;
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (linkedCancellation.Token.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }

            throw;
        }

        if (process.ExitCode == 0)
        {
            return await standardOutput;
        }

        if (allowFailure)
        {
            return null;
        }

        throw new InvalidOperationException(
            $"Unable to configure KWin Wayland integration. gdbus exited with {process.ExitCode}: " +
            $"{await standardError} {await standardOutput}");
    }
}
