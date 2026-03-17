using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using WpfApplication = System.Windows.Application;

namespace Pop.App;

internal static class AppIconProvider
{
    private static readonly Uri WindowIconUri = new("pack://application:,,,/Assets/Pop.ico", UriKind.Absolute);
    private static readonly Uri TrayIconUri = new("pack://application:,,,/Assets/Pop.ico", UriKind.Absolute);

    public static BitmapFrame CreateWindowIcon()
    {
        return BitmapFrame.Create(WindowIconUri);
    }

    public static Icon CreateTrayIcon()
    {
        var resource = WpfApplication.GetResourceStream(TrayIconUri)
            ?? throw new InvalidOperationException($"Unable to load icon resource '{TrayIconUri}'.");

        using var stream = resource.Stream;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        using var icon = new Icon(buffer);
        return (Icon)icon.Clone();
    }
}
