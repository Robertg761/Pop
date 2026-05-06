using Pop.App.Linux;

var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
LinuxPopHost? host = null;

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.TrySetResult();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) => host?.Dispose();

try
{
    host = new LinuxPopHost();
    await host.InitializeAsync();
    Console.WriteLine("Pop for Linux is running. Press Ctrl+C to exit.");
    await shutdown.Task;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Pop couldn't start: {exception.Message}");
    Environment.ExitCode = 1;
}
finally
{
    host?.Dispose();
}
