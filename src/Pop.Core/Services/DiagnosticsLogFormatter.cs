using System.Text.Json;
using System.Text.Json.Serialization;
using Pop.Core.Models;
using Pop.Core.Serialization;

namespace Pop.Core.Services;

public static class DiagnosticsLogFormatter
{
    public static string Format(DiagnosticEvent diagnosticEvent)
    {
        // Build the field map defensively: two distinct keys can collide once truncated to
        // 48 chars, and ToDictionary would throw on the duplicate — a crash on the drag hot
        // path. Last-writer-wins keeps formatting total.
        Dictionary<string, string?>? safeFields = null;
        if (diagnosticEvent.Fields is { } fields)
        {
            safeFields = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var pair in fields.Take(16))
            {
                safeFields[Truncate(pair.Key, 48)] = pair.Value is null ? null : Truncate(pair.Value, 180);
            }
        }

        var payload = new DiagnosticsLogPayload(
            diagnosticEvent.Timestamp.ToUniversalTime().ToString("O"),
            Truncate(diagnosticEvent.Category, 48),
            Truncate(diagnosticEvent.Message, 240),
            safeFields);

        return JsonSerializer.Serialize(payload, PopJsonContext.Default.DiagnosticsLogPayload);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    internal sealed record DiagnosticsLogPayload(
        [property: JsonPropertyName("timestamp")] string Timestamp,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("fields")] Dictionary<string, string?>? Fields);
}
