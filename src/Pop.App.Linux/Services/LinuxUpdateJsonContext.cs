using System.Text.Json.Serialization;

namespace Pop.App.Linux.Services;

[JsonSerializable(typeof(PreparedAppImageUpdate))]
internal partial class LinuxUpdateJsonContext : JsonSerializerContext;
