using Microsoft.Win32;
using Pop.Platform.Abstractions.Startup;

namespace Pop.App.Windows.Platform.Startup;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Pop";

    public void SetLaunchAtStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        key.SetValue(ValueName, $"\"{executablePath}\"");
    }
}
