using System.IO;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.App.Windows.Services;

public sealed class DiagnosticsLogService : IDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pop",
        "diagnostics.log");

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        _ = WriteInternalAsync(diagnosticEvent);
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();
        _writeGate.Dispose();
        _disposeCancellation.Dispose();
    }

    private async Task WriteInternalAsync(DiagnosticEvent diagnosticEvent)
    {
        try
        {
            var line = DiagnosticsLogFormatter.Format(diagnosticEvent) + Environment.NewLine;
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);

            await _writeGate.WaitAsync(_disposeCancellation.Token);
            try
            {
                await File.AppendAllTextAsync(_logPath, line, _disposeCancellation.Token);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
