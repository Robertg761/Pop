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
    private readonly string _scriptPath = Path.Combine(LinuxPaths.ConfigDirectory, "kwin", "pop-wayland.js");

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
        await ReloadAsync(settings, cancellationToken);
    }

    public async Task ReloadAsync(AppSettings settings, CancellationToken cancellationToken)
    {
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

    public void Dispose()
    {
        try
        {
            RunGdbusAsync(CancellationToken.None, allowFailure: true, "call", "--session",
                "--dest", "org.kde.KWin",
                "--object-path", "/Scripting",
                "--method", "org.kde.kwin.Scripting.unloadScript",
                PluginName).GetAwaiter().GetResult();
        }
        catch
        {
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

    private static async Task RunGdbusAsync(
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] arguments)
    {
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
                return;
            }

            throw new InvalidOperationException(
                "Pop's KWin Wayland integration requires the 'gdbus' tool (part of glib2). Install it and try again.",
                exception);
        }

        using var _ = process;
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0 || allowFailure)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unable to configure KWin Wayland integration. gdbus exited with {process.ExitCode}: " +
            $"{await standardError} {await standardOutput}");
    }
}
