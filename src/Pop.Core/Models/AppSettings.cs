namespace Pop.Core.Models;

public sealed record AppSettings
{
    public bool Enabled { get; init; } = true;

    public bool LaunchAtStartup { get; init; }

    public double ThrowVelocityThresholdPxPerSec { get; init; } = 1800;

    public double HorizontalDominanceRatio { get; init; } = 1.75;

    public int GlideDurationMs { get; init; } = 220;

    public bool EnableDiagnostics { get; init; }
}
