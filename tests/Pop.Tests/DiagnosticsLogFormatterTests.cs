using System.Text.Json;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class DiagnosticsLogFormatterTests
{
    [Fact]
    public void Format_TruncatesLargePayloads_AndProducesValidJson()
    {
        var diagnosticEvent = new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            "drag-release",
            new string('a', 500),
            new Dictionary<string, string?>
            {
                ["reason"] = new string('b', 500),
                ["detail"] = "supported"
            });

        var json = DiagnosticsLogFormatter.Format(diagnosticEvent);
        using var document = JsonDocument.Parse(json);

        var message = document.RootElement.GetProperty("message").GetString();
        Assert.NotNull(message);
        Assert.True(message!.Length <= 240);
    }

    [Fact]
    public void Format_DoesNotThrow_WhenFieldKeysCollideAfterTruncation()
    {
        var prefix = new string('k', 48);
        var diagnosticEvent = new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            "drag-release",
            "message",
            new Dictionary<string, string?>
            {
                [prefix + "-one"] = "1",
                [prefix + "-two"] = "2"
            });

        var json = DiagnosticsLogFormatter.Format(diagnosticEvent);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("fields", out var fields));
        Assert.Single(fields.EnumerateObject());
    }
}
