using System.Diagnostics;
using System.Text.Json;

namespace Pop.App.Linux.Services;

internal sealed record PreparedAppImageUpdate(
    string Version,
    string StagedAppImagePath,
    string WorkingDirectoryPath);

internal sealed class PreparedAppImageUpdateInstaller
{
    private readonly string _baseDirectoryPath;
    private readonly string _targetAppImagePath;

    public PreparedAppImageUpdateInstaller(
        string? baseDirectoryPath = null,
        string? targetAppImagePath = null)
    {
        _baseDirectoryPath = baseDirectoryPath ?? Path.Combine(LinuxPaths.ConfigDirectory, "Updates");
        _targetAppImagePath = targetAppImagePath
            ?? Environment.GetEnvironmentVariable("APPIMAGE")
            ?? string.Empty;
    }

    public bool IsSupportedInstallation =>
        OperatingSystem.IsLinux()
        && !string.IsNullOrWhiteSpace(_targetAppImagePath)
        && File.Exists(_targetAppImagePath)
        && IsDirectoryWritable(Path.GetDirectoryName(_targetAppImagePath));

    public string UnsupportedReason
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_targetAppImagePath))
            {
                return "Install Pop from the Linux AppImage release to enable in-app updates. The tar.gz package can be updated manually from GitHub Releases.";
            }

            var parentDirectory = Path.GetDirectoryName(_targetAppImagePath) ?? _targetAppImagePath;
            return $"Install Pop from a writable Linux AppImage location to enable in-app updates. Current install location: {parentDirectory}";
        }
    }

    public string CreateDownloadPath(string version)
    {
        var downloadsDirectory = Path.Combine(_baseDirectoryPath, "Downloads");
        Directory.CreateDirectory(downloadsDirectory);
        return Path.Combine(downloadsDirectory, $"Pop-linux-x64-{version}-{Guid.NewGuid():N}.AppImage");
    }

    public PreparedAppImageUpdate? LoadPreparedUpdate()
    {
        if (!File.Exists(MetadataPath))
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize(
            File.ReadAllText(MetadataPath),
            LinuxUpdateJsonContext.Default.PreparedAppImageUpdate);
        if (metadata is null || !File.Exists(metadata.StagedAppImagePath))
        {
            if (metadata is not null && Directory.Exists(metadata.WorkingDirectoryPath))
            {
                Directory.Delete(metadata.WorkingDirectoryPath, recursive: true);
            }

            TryDeleteMetadata();
            return null;
        }

        return metadata;
    }

    public void RemoveObsoletePreparedUpdate(string currentVersion)
    {
        var preparedUpdate = LoadPreparedUpdate();
        if (preparedUpdate is null)
        {
            return;
        }

        if (!AppVersion.TryParse(preparedUpdate.Version, out var preparedVersion)
            || !AppVersion.TryParse(currentVersion, out var installedVersion)
            || preparedVersion <= installedVersion)
        {
            ClearPreparedUpdate();
        }
    }

    public PreparedAppImageUpdate PrepareUpdate(string downloadedAppImagePath, string version)
    {
        ClearPreparedUpdate();

        var workingDirectoryPath = Path.Combine(_baseDirectoryPath, version);
        var stagedAppImagePath = Path.Combine(workingDirectoryPath, $"Pop-linux-x64-{version}.AppImage");
        Directory.CreateDirectory(workingDirectoryPath);

        if (File.Exists(stagedAppImagePath))
        {
            File.Delete(stagedAppImagePath);
        }

        File.Move(downloadedAppImagePath, stagedAppImagePath);
        MakeExecutable(stagedAppImagePath);

        var preparedUpdate = new PreparedAppImageUpdate(version, stagedAppImagePath, workingDirectoryPath);
        Directory.CreateDirectory(_baseDirectoryPath);
        File.WriteAllText(
            MetadataPath,
            JsonSerializer.Serialize(preparedUpdate, LinuxUpdateJsonContext.Default.PreparedAppImageUpdate));
        return preparedUpdate;
    }

    public void InstallPreparedUpdate(PreparedAppImageUpdate preparedUpdate)
    {
        if (!File.Exists(preparedUpdate.StagedAppImagePath))
        {
            throw new FileNotFoundException("The downloaded update could not be prepared.", preparedUpdate.StagedAppImagePath);
        }

        Directory.CreateDirectory(_baseDirectoryPath);
        File.WriteAllText(InstallerScriptPath, InstallerScript);
        MakeExecutable(InstallerScriptPath);

        using var process = new Process();
        process.StartInfo.FileName = "/bin/bash";
        process.StartInfo.ArgumentList.Add(InstallerScriptPath);
        process.StartInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        process.StartInfo.ArgumentList.Add(_targetAppImagePath);
        process.StartInfo.ArgumentList.Add(preparedUpdate.StagedAppImagePath);
        process.StartInfo.ArgumentList.Add(preparedUpdate.WorkingDirectoryPath);
        process.StartInfo.ArgumentList.Add(MetadataPath);
        process.Start();
    }

    private void ClearPreparedUpdate()
    {
        var preparedUpdate = LoadPreparedUpdate();
        if (preparedUpdate is not null && Directory.Exists(preparedUpdate.WorkingDirectoryPath))
        {
            Directory.Delete(preparedUpdate.WorkingDirectoryPath, recursive: true);
        }

        TryDeleteMetadata();
    }

    private void TryDeleteMetadata()
    {
        if (File.Exists(MetadataPath))
        {
            File.Delete(MetadataPath);
        }
    }

    private static bool IsDirectoryWritable(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        var probePath = Path.Combine(directoryPath, $".pop-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
            }
        }
    }

    private static void MakeExecutable(string path)
    {
        using var process = new Process();
        process.StartInfo.FileName = "/bin/chmod";
        process.StartInfo.ArgumentList.Add("755");
        process.StartInfo.ArgumentList.Add(path);
        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"chmod failed for {path}.");
        }
    }

    private string MetadataPath => Path.Combine(_baseDirectoryPath, "prepared-update.json");

    private string InstallerScriptPath => Path.Combine(_baseDirectoryPath, "install-update.sh");

    private static string InstallerScript =>
        """
        #!/usr/bin/env bash
        set -euo pipefail

        APP_PID="$1"
        TARGET_APP="$2"
        STAGED_APP="$3"
        WORK_DIR="$4"
        METADATA_FILE="$5"
        BACKUP_APP="${TARGET_APP}.previous"

        while kill -0 "$APP_PID" 2>/dev/null; do
          sleep 0.2
        done

        rm -f "$BACKUP_APP"
        if [[ -e "$TARGET_APP" ]]; then
          mv "$TARGET_APP" "$BACKUP_APP"
        fi

        if mv "$STAGED_APP" "$TARGET_APP"; then
          chmod +x "$TARGET_APP"
          rm -f "$BACKUP_APP"
        else
          rm -f "$TARGET_APP"
          if [[ -e "$BACKUP_APP" ]]; then
            mv "$BACKUP_APP" "$TARGET_APP"
          fi
          exit 1
        fi

        rm -rf "$WORK_DIR"
        rm -f "$METADATA_FILE"
        nohup "$TARGET_APP" >/dev/null 2>&1 &
        """;
}
