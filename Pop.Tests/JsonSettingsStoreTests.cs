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
        Assert.True(settings.ShowOverlay);
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
            ThrowVelocityThresholdPxPerSec = 2300,
            HorizontalDominanceRatio = 2.25,
            GlideDurationMs = 340,
            ShowOverlay = false
        };

        await store.SaveAsync(original);
        var restored = await store.LoadAsync();

        Assert.Equal(original, restored);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
