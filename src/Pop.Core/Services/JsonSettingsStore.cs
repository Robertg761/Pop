using System.Text.Json;
using Pop.Core.Interfaces;
using Pop.Core.Models;
using Pop.Core.Serialization;

namespace Pop.Core.Services;

public sealed class JsonSettingsStore(string? settingsDirectory = null, string fileName = "settings.json") : ISettingsStore
{
    private readonly string _settingsDirectory = settingsDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pop");

    private readonly string _fileName = fileName;

    public string SettingsPath => Path.Combine(_settingsDirectory, _fileName);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync(stream, PopJsonContext.Default.AppSettings, cancellationToken);
            return settings ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settingsDirectory);
        var temporaryPath = Path.Combine(_settingsDirectory, $"{_fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, PopJsonContext.Default.AppSettings, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(SettingsPath) && OperatingSystem.IsWindows())
            {
                File.Replace(temporaryPath, SettingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }

            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
