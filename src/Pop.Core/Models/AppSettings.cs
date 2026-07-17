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

    /// <summary>
    /// Returns a copy with numeric fields clamped to safe ranges. Values loaded from a
    /// hand-edited or corrupt <c>settings.json</c> are not otherwise validated, and
    /// out-of-range values (e.g. a zero dominance ratio or a negative glide duration)
    /// break gesture qualification and animation planning downstream.
    /// </summary>
    public AppSettings Normalized()
    {
        var velocity = double.IsFinite(ThrowVelocityThresholdPxPerSec)
            ? Math.Clamp(ThrowVelocityThresholdPxPerSec, 50d, 100_000d)
            : Default.ThrowVelocityThresholdPxPerSec;

        var dominance = double.IsFinite(HorizontalDominanceRatio)
            ? Math.Clamp(HorizontalDominanceRatio, 1d, 50d)
            : Default.HorizontalDominanceRatio;

        var glide = Math.Clamp(GlideDurationMs, 0, 5_000);

        if (velocity == ThrowVelocityThresholdPxPerSec
            && dominance == HorizontalDominanceRatio
            && glide == GlideDurationMs)
        {
            return this;
        }

        return this with
        {
            ThrowVelocityThresholdPxPerSec = velocity,
            HorizontalDominanceRatio = dominance,
            GlideDurationMs = glide
        };
    }
}
