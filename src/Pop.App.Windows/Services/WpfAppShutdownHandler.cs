using Application = System.Windows.Application;

namespace Pop.App.Windows.Services;

internal sealed class WpfAppShutdownHandler : IAppShutdownHandler
{
    public void RequestShutdown()
    {
        Application.Current.Shutdown();
    }
}
