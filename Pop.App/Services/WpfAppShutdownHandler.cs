using Application = System.Windows.Application;

namespace Pop.App.Services;

internal sealed class WpfAppShutdownHandler : IAppShutdownHandler
{
    public void RequestShutdown()
    {
        Application.Current.Shutdown();
    }
}
