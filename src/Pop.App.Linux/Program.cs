using Avalonia;

namespace Pop.App.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<PopLinuxApp>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
