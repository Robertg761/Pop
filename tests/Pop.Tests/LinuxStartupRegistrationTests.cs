using Pop.App.Linux.Platform.Startup;

namespace Pop.Tests;

public sealed class LinuxStartupRegistrationTests : IDisposable
{
    private readonly string _autostartDirectory =
        Path.Combine(Path.GetTempPath(), "pop-startup-tests-" + Guid.NewGuid().ToString("N"));

    private string DesktopFilePath => Path.Combine(_autostartDirectory, "pop.desktop");

    [Fact]
    public void TrySetLaunchAtStartup_Enabled_WritesDesktopEntryWithExec()
    {
        var registration = new LinuxStartupRegistration(_autostartDirectory, "\"/opt/pop/Pop.AppImage\"");

        var applied = registration.TrySetLaunchAtStartup(true);

        Assert.True(applied);
        var content = File.ReadAllText(DesktopFilePath);
        Assert.Contains("[Desktop Entry]", content);
        Assert.Contains("Exec=\"/opt/pop/Pop.AppImage\"", content);
        Assert.Contains("Type=Application", content);
    }

    [Fact]
    public void TrySetLaunchAtStartup_Disabled_RemovesEntryAndSucceedsWhenAbsent()
    {
        var registration = new LinuxStartupRegistration(_autostartDirectory, "\"/usr/bin/pop\"");
        registration.TrySetLaunchAtStartup(true);

        Assert.True(registration.TrySetLaunchAtStartup(false));
        Assert.False(File.Exists(DesktopFilePath));

        // Disabling again with no entry present is still a success.
        Assert.True(registration.TrySetLaunchAtStartup(false));
    }

    [Fact]
    public void IsLaunchAtStartupEnabled_ReflectsEntryPresence()
    {
        var registration = new LinuxStartupRegistration(_autostartDirectory, "\"/usr/bin/pop\"");

        Assert.Equal(false, registration.IsLaunchAtStartupEnabled());

        registration.TrySetLaunchAtStartup(true);
        Assert.Equal(true, registration.IsLaunchAtStartupEnabled());

        registration.TrySetLaunchAtStartup(false);
        Assert.Equal(false, registration.IsLaunchAtStartupEnabled());
    }

    [Fact]
    public void QuoteExecArgument_EscapesReservedCharacters()
    {
        var quoted = LinuxStartupRegistration.QuoteExecArgument("/pa th/$weird\"na`me\\x");

        Assert.Equal("\"/pa th/\\$weird\\\"na\\`me\\\\x\"", quoted);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_autostartDirectory))
            {
                Directory.Delete(_autostartDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
