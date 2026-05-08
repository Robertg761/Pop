using System.Diagnostics;
using System.Reflection;
using Pop.App.Linux.Services;

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

        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(waylandDisplay);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await WriteScriptAsync(cancellationToken);

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

    private async Task WriteScriptAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_scriptPath)!);

        await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException($"Unable to load embedded KWin script '{ScriptResourceName}'.");
        await using var output = File.Create(_scriptPath);
        await resource.CopyToAsync(output, cancellationToken);
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start gdbus.");
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
