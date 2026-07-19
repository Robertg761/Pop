using System.Threading;
using Velopack;

namespace Pop.App.Windows;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\Pop.SingleInstance";

    // Held for the process lifetime; releasing it lets a second instance start.
    private static Mutex? _singleInstanceMutex;

    private PopHost? _host;

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Another Pop instance already owns the tray icon and the global mouse hook.
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        StartHost(e);
    }

    private async void StartHost(System.Windows.StartupEventArgs e)
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

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Pop is a background tray utility with no visible window: a stray dispatcher exception
        // must fail soft rather than tear down the process with a WER dialog.
        LogHostError("dispatcher", e.Exception);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogHostError("appdomain", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogHostError("task", e.Exception);
        e.SetObserved();
    }

    private static void LogHostError(string source, Exception? exception)
    {
        try
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pop");
            System.IO.Directory.CreateDirectory(directory);
            var line = $"{DateTimeOffset.Now:O} [{source}] {exception}{Environment.NewLine}";
            System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "host-errors.log"), line);
        }
        catch
        {
            // Best-effort logging only; never let the safety net throw.
        }
    }
}
