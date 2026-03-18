using System.Text.Json.Serialization;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Core.Serialization;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(DiagnosticsLogFormatter.DiagnosticsLogPayload))]
internal partial class PopJsonContext : JsonSerializerContext
{
}
