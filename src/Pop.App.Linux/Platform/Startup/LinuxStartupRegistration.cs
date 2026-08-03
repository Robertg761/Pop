using System.Text;
using Pop.Platform.Abstractions.Startup;

namespace Pop.App.Linux.Platform.Startup;

/// <summary>
/// Registers Pop for launch at login by writing an XDG autostart entry to
/// ~/.config/autostart/pop.desktop (or removing it).
/// </summary>
public sealed class LinuxStartupRegistration : IStartupRegistration
{
    private const string DesktopFileName = "pop.desktop";
    private readonly string _autostartDirectory;
    private readonly string? _executableCommandOverride;

    public LinuxStartupRegistration()
        : this(GetDefaultAutostartDirectory())
    {
    }

    // Test hook: the directory and Exec command are injectable so tests never touch ~/.config.
    internal LinuxStartupRegistration(string autostartDirectory, string? executableCommand = null)
    {
        _autostartDirectory = autostartDirectory;
        _executableCommandOverride = executableCommand;
    }

    private string DesktopFilePath => Path.Combine(_autostartDirectory, DesktopFileName);

    public bool TrySetLaunchAtStartup(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(DesktopFilePath); // no-op when the entry does not exist
                return true;
            }

            var command = _executableCommandOverride ?? GetExecutableCommand();
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            Directory.CreateDirectory(_autostartDirectory);
            File.WriteAllText(
                DesktopFilePath,
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Pop\n" +
                "Comment=Throw windows to the side to snap them\n" +
                $"Exec={command}\n" +
                "Terminal=false\n" +
                "X-GNOME-Autostart-enabled=true\n");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool? IsLaunchAtStartupEnabled()
    {
        try
        {
            return File.Exists(DesktopFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string GetDefaultAutostartDirectory()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configRoot = !string.IsNullOrWhiteSpace(xdgConfigHome)
            ? xdgConfigHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configRoot, "autostart");
    }

    private static string? GetExecutableCommand()
    {
        // An AppImage must be relaunched via the image path, not the extracted runtime binary.
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath) && File.Exists(appImagePath))
        {
            return QuoteExecArgument(appImagePath);
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            try
            {
                processPath = File.ResolveLinkTarget("/proc/self/exe", returnFinalTarget: true)?.FullName;
            }
            catch (IOException)
            {
            }
        }

        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        // A framework-dependent launch ("dotnet Pop.dll") must restart the managed entry point,
        // not the bare dotnet host. (args[0] is the entry assembly path in that case; avoids
        // Assembly.Location, which is empty in single-file publishes.)
        if (string.Equals(Path.GetFileName(processPath), "dotnet", StringComparison.Ordinal))
        {
            var entryAssemblyPath = Environment.GetCommandLineArgs().FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(entryAssemblyPath) && File.Exists(entryAssemblyPath))
            {
                return $"{QuoteExecArgument(processPath)} {QuoteExecArgument(Path.GetFullPath(entryAssemblyPath))}";
            }
        }

        return QuoteExecArgument(processPath);
    }

    // Desktop Entry spec Exec quoting: wrap in double quotes and backslash-escape the reserved
    // characters ("), (`), ($) and (\).
    internal static string QuoteExecArgument(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            if (character is '"' or '`' or '$' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append('"');
        return builder.ToString();
    }
}
