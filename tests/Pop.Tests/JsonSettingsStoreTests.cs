using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "Pop.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenSettingsFileIsMissing()
    {
        var store = new JsonSettingsStore(_tempDirectory);

        var settings = await store.LoadAsync();

        Assert.True(settings.Enabled);
        Assert.False(settings.EnableDiagnostics);
        Assert.Equal(220, settings.GlideDurationMs);
    }

    [Fact]
    public async Task SaveAsync_AndLoadAsync_RoundTripSettings()
    {
        var store = new JsonSettingsStore(_tempDirectory);
        var original = new AppSettings
        {
            Enabled = false,
            LaunchAtStartup = true,
            EnableDiagnostics = true,
            ThrowVelocityThresholdPxPerSec = 2300,
            HorizontalDominanceRatio = 2.25,
            GlideDurationMs = 340
        };

        await store.SaveAsync(original);
        var restored = await store.LoadAsync();

        Assert.Equal(original, restored);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenSettingsFileContainsInvalidJson()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "settings.json"), "{ invalid json");
        var store = new JsonSettingsStore(_tempDirectory);

        var settings = await store.LoadAsync();

        Assert.Equal(new AppSettings(), settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
