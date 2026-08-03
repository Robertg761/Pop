using System.Text.Json;
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

        Assert.Equal(AppSettings.Default, settings);
    }

    [Fact]
    public async Task LoadAsync_BacksUpCorruptFile_BeforeFallingBackToDefaults()
    {
        Directory.CreateDirectory(_tempDirectory);
        var settingsPath = Path.Combine(_tempDirectory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ invalid json");
        var store = new JsonSettingsStore(_tempDirectory);

        await store.LoadAsync();

        Assert.True(File.Exists(settingsPath + ".corrupt"));
        Assert.Equal("{ invalid json", await File.ReadAllTextAsync(settingsPath + ".corrupt"));
    }

    [Fact]
    public async Task LoadAsync_ClampsOutOfRangeValues()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "settings.json"),
            "{\"HorizontalDominanceRatio\": 0, \"GlideDurationMs\": -100, \"ThrowVelocityThresholdPxPerSec\": 5}");
        var store = new JsonSettingsStore(_tempDirectory);

        var settings = await store.LoadAsync();

        Assert.True(settings.HorizontalDominanceRatio >= 1d);
        Assert.True(settings.GlideDurationMs >= 0);
        Assert.True(settings.ThrowVelocityThresholdPxPerSec >= 50d);
    }

    [Fact]
    public async Task SaveAsync_PreservesUnknownJsonKeys_AcrossLoadSaveRoundTrip()
    {
        Directory.CreateDirectory(_tempDirectory);
        var settingsPath = Path.Combine(_tempDirectory, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            "{\"Enabled\": false, \"FutureSetting\": \"keep-me\", \"FutureNumber\": 7}");
        var store = new JsonSettingsStore(_tempDirectory);

        var loaded = await store.LoadAsync();
        await store.SaveAsync(loaded);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        Assert.False(document.RootElement.GetProperty("Enabled").GetBoolean());
        Assert.Equal("keep-me", document.RootElement.GetProperty("FutureSetting").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("FutureNumber").GetInt32());
    }

    [Fact]
    public async Task SaveAsync_WritesSchemaVersion_AndLoadAsyncToleratesIt()
    {
        var store = new JsonSettingsStore(_tempDirectory);

        await store.SaveAsync(AppSettings.Default);

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_tempDirectory, "settings.json")));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());

        var reloaded = await store.LoadAsync();
        Assert.Equal(AppSettings.Default, reloaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
