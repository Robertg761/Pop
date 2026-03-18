namespace Pop.Core.Models;

public sealed record DiagnosticEvent(
    DateTimeOffset Timestamp,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string?>? Fields = null);
