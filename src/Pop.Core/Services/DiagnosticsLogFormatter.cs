using System.Text.Json;
using Pop.Core.Models;

namespace Pop.Core.Services;

public static class DiagnosticsLogFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static string Format(DiagnosticEvent diagnosticEvent)
    {
        var safeFields = diagnosticEvent.Fields?
            .Take(16)
            .ToDictionary(
                pair => Truncate(pair.Key, 48),
                pair => pair.Value is null ? null : Truncate(pair.Value, 180));

        var payload = new
        {
            timestamp = diagnosticEvent.Timestamp.ToUniversalTime().ToString("O"),
            category = Truncate(diagnosticEvent.Category, 48),
            message = Truncate(diagnosticEvent.Message, 240),
            fields = safeFields
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
