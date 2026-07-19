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

    // Serialize concurrent SaveAsync calls (e.g. a tray toggle racing a settings-window save):
    // two writers otherwise race File.Replace/File.Move on the same destination.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public string SettingsPath => Path.Combine(_settingsDirectory, _fileName);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return AppSettings.Default;
            }

            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync(stream, PopJsonContext.Default.AppSettings, cancellationToken);
            return (settings ?? AppSettings.Default).Normalized();
        }
        catch (JsonException)
        {
            // The file exists but is not valid JSON (e.g. a truncated write). Preserve the
            // evidence before the next SaveAsync overwrites it, then fall back to defaults.
            TryBackupCorruptFile();
            return AppSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // File locked by AV/backup tooling, broken permissions, or deleted between the
            // File.Exists check and the open. Fail soft: the host must still start.
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
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
        finally
        {
            _saveGate.Release();
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            var backupPath = SettingsPath + ".corrupt";
            File.Copy(SettingsPath, backupPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Best-effort only; never let backup failure mask the fall-back to defaults.
        }
    }
}
