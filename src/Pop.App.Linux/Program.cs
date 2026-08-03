using Avalonia;

namespace Pop.App.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Two instances would run duplicate drag pollers and race the KWin script load/unload.
        using var instanceGuard = SingleInstanceGuard.TryAcquire();
        if (instanceGuard is null)
        {
            Console.Error.WriteLine("Pop is already running. Use its tray icon or settings window to configure it.");
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<PopLinuxApp>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
