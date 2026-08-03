using System.IO;
using System.Threading.Channels;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.App.Windows.Services;

public sealed class DiagnosticsLogService : IDisposable
{
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly Channel<DiagnosticEvent> _events = Channel.CreateUnbounded<DiagnosticEvent>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writerTask;
    private readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pop",
        "diagnostics.log");

    public DiagnosticsLogService()
    {
        _writerTask = Task.Run(WriteEventsAsync);
    }

    // Non-blocking enqueue: callers can sit on hot paths (the mouse-hook processing loop), so
    // all formatting and file I/O happens on the single background writer, in enqueue order.
    public void Write(DiagnosticEvent diagnosticEvent)
    {
        _events.Writer.TryWrite(diagnosticEvent);
    }

    public void Dispose()
    {
        _events.Writer.TryComplete();

        try
        {
            _writerTask.Wait(DisposeDrainTimeout);
        }
        catch (AggregateException)
        {
        }
    }

    private async Task WriteEventsAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        }
        catch
        {
            // Fall through and still drain the channel; each append attempt fails quietly.
        }

        await foreach (var diagnosticEvent in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var line = DiagnosticsLogFormatter.Format(diagnosticEvent) + Environment.NewLine;
                await File.AppendAllTextAsync(_logPath, line).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort logging only.
            }
        }
    }
}
