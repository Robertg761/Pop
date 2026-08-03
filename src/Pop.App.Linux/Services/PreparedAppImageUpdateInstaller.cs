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

        PreparedAppImageUpdate? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize(
                File.ReadAllText(MetadataPath),
                LinuxUpdateJsonContext.Default.PreparedAppImageUpdate);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable metadata must not wedge updates forever; discard it along
            // with any orphaned staging directories and start over on the next check.
            RemoveCorruptPreparedUpdate();
            return null;
        }

        if (metadata is null || !File.Exists(metadata.StagedAppImagePath))
        {
            if (metadata is not null)
            {
                TryDeleteDirectory(metadata.WorkingDirectoryPath);
            }

            TryDeleteMetadata();
            return null;
        }

        return metadata;
    }

    public void CleanUpAbandonedDownloads()
    {
        // Staged updates are moved out of Downloads, so anything still in there is a leftover
        // from a failed or cancelled download.
        var downloadsDirectory = Path.Combine(_baseDirectoryPath, "Downloads");
        if (!Directory.Exists(downloadsDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(downloadsDirectory))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
            }
        }
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
        var temporaryMetadataPath = $"{MetadataPath}.tmp";
        File.WriteAllText(
            temporaryMetadataPath,
            JsonSerializer.Serialize(preparedUpdate, LinuxUpdateJsonContext.Default.PreparedAppImageUpdate));
        File.Move(temporaryMetadataPath, MetadataPath, overwrite: true);
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
        process.StartInfo.FileName = "/usr/bin/env";
        process.StartInfo.ArgumentList.Add(File.Exists("/bin/bash") || File.Exists("/usr/bin/bash") ? "bash" : "sh");
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
        if (preparedUpdate is not null)
        {
            TryDeleteDirectory(preparedUpdate.WorkingDirectoryPath);
        }

        TryDeleteMetadata();
    }

    private void RemoveCorruptPreparedUpdate()
    {
        TryDeleteMetadata();

        // The metadata can no longer tell us which staging directory it referenced, so sweep
        // every staging directory (everything under the base directory except Downloads).
        if (!Directory.Exists(_baseDirectoryPath))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(_baseDirectoryPath))
        {
            if (string.Equals(Path.GetFileName(directory), "Downloads", StringComparison.Ordinal))
            {
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    private void TryDeleteMetadata()
    {
        try
        {
            if (File.Exists(MetadataPath))
            {
                File.Delete(MetadataPath);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
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
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException($"Couldn't mark {path} as executable.", exception);
        }
    }

    private string MetadataPath => Path.Combine(_baseDirectoryPath, "prepared-update.json");

    private string InstallerScriptPath => Path.Combine(_baseDirectoryPath, "install-update.sh");

    private static string InstallerScript =>
        """
        #!/bin/sh
        set -eu

        APP_PID="$1"
        TARGET_APP="$2"
        STAGED_APP="$3"
        WORK_DIR="$4"
        METADATA_FILE="$5"
        BACKUP_APP="${TARGET_APP}.previous"

        # Capture the process start time so a recycled PID isn't mistaken for the app.
        read_start_time() {
          sed 's/^.*) //' "/proc/$APP_PID/stat" 2>/dev/null | awk '{print $20}' || true
        }

        APP_START_TIME="$(read_start_time)"

        # Wait for the app to exit, but give up after ~120s; the prepared update stays
        # staged so it can be applied on a later attempt.
        WAITED_TICKS=0
        while kill -0 "$APP_PID" 2>/dev/null; do
          if [ -n "$APP_START_TIME" ]; then
            CURRENT_START_TIME="$(read_start_time)"
            if [ "$CURRENT_START_TIME" != "$APP_START_TIME" ]; then
              break
            fi
          fi
          if [ "$WAITED_TICKS" -ge 600 ]; then
            exit 1
          fi
          WAITED_TICKS=$((WAITED_TICKS + 1))
          sleep 0.2
        done

        rm -f "$BACKUP_APP"
        if [ -e "$TARGET_APP" ]; then
          mv "$TARGET_APP" "$BACKUP_APP"
        fi

        if mv "$STAGED_APP" "$TARGET_APP"; then
          chmod +x "$TARGET_APP"
          rm -f "$BACKUP_APP"
        else
          rm -f "$TARGET_APP"
          if [ -e "$BACKUP_APP" ]; then
            mv "$BACKUP_APP" "$TARGET_APP"
          fi
          exit 1
        fi

        rm -rf "$WORK_DIR"
        rm -f "$METADATA_FILE"
        nohup "$TARGET_APP" >/dev/null 2>&1 &
        """;
}
