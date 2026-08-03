using System.Diagnostics;

namespace Pop.App.Linux;

/// <summary>
/// Best-effort desktop notifications via notify-send. Pop is a tray-first app that is usually
/// launched from a .desktop entry, where Console output is invisible; these notifications are
/// the only way some failures ever reach the user. All failures are swallowed.
/// </summary>
internal static class DesktopNotifier
{
    public static bool TryNotify(string title, string message)
    {
        try
        {
            var startInfo = new ProcessStartInfo("notify-send")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--app-name=Pop");
            startInfo.ArgumentList.Add(title);
            startInfo.ArgumentList.Add(message);
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}
