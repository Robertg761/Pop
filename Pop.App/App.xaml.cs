namespace Pop.App;

public partial class App : System.Windows.Application
{
    private PopHost? _host;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = new PopHost();
            await _host.InitializeAsync();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Pop couldn't start.\n\n{exception.Message}",
                "Pop Startup Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            Shutdown();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
