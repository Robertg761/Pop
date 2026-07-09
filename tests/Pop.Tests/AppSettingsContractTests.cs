using System.Text.Json;
using Pop.Core.Models;

namespace Pop.Tests;

public sealed class AppSettingsContractTests
{
    [Fact]
    public void DefaultSettings_MatchSharedContract()
    {
        var contract = LoadContract();
        var defaults = contract.GetProperty("defaults");
        var settings = AppSettings.Default;

        Assert.Equal(defaults.GetProperty("Enabled").GetBoolean(), settings.Enabled);
        Assert.Equal(defaults.GetProperty("LaunchAtStartup").GetBoolean(), settings.LaunchAtStartup);
        Assert.Equal(defaults.GetProperty("ThrowVelocityThresholdPxPerSec").GetDouble(), settings.ThrowVelocityThresholdPxPerSec);
        Assert.Equal(defaults.GetProperty("HorizontalDominanceRatio").GetDouble(), settings.HorizontalDominanceRatio);
        Assert.Equal(defaults.GetProperty("GlideDurationMs").GetInt32(), settings.GlideDurationMs);
        Assert.Equal(defaults.GetProperty("EnableDiagnostics").GetBoolean(), settings.EnableDiagnostics);
    }

    [Fact]
    public void SerializedSettings_UseCanonicalJsonPropertyNames()
    {
        var contract = LoadContract();
        var expectedNames = contract.GetProperty("jsonPropertyNames")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(AppSettings.Default));
        var actualNames = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedNames, actualNames);
    }

    [Fact]
    public void ContractFile_ListsExactlyTheKnownSettingsFields()
    {
        var contract = LoadContract();
        var names = contract.GetProperty("jsonPropertyNames")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();

        Assert.Contains("Enabled", names);
        Assert.Contains("LaunchAtStartup", names);
        Assert.Contains("ThrowVelocityThresholdPxPerSec", names);
        Assert.Contains("HorizontalDominanceRatio", names);
        Assert.Contains("GlideDurationMs", names);
        Assert.Contains("EnableDiagnostics", names);
        Assert.Equal(6, names.Length);
    }

    private static JsonElement LoadContract()
    {
        var path = FindContractPath();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static string FindContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "contracts", "app-settings.contract.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate contracts/app-settings.contract.json from the test output directory.");
    }
}
