using System.Text.Json.Serialization;

namespace Pop.Core.Models;

/// <summary>
/// Cross-platform settings document. JSON property names are part of the public contract
/// (see contracts/app-settings.contract.json). macOS Swift CodingKeys must match exactly.
/// </summary>
public sealed record AppSettings
{
    public static AppSettings Default { get; } = new();

    [JsonPropertyName("Enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("LaunchAtStartup")]
    public bool LaunchAtStartup { get; init; }

    [JsonPropertyName("ThrowVelocityThresholdPxPerSec")]
    public double ThrowVelocityThresholdPxPerSec { get; init; } = 1800;

    [JsonPropertyName("HorizontalDominanceRatio")]
    public double HorizontalDominanceRatio { get; init; } = 1.75;

    [JsonPropertyName("GlideDurationMs")]
    public int GlideDurationMs { get; init; } = 220;

    [JsonPropertyName("EnableDiagnostics")]
    public bool EnableDiagnostics { get; init; }
}
