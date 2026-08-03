using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pop.App.Linux.Services;

internal sealed record AppRelease(string Version, string AssetName, Uri AssetUri, Uri? ChecksumUri);

internal sealed class GitHubReleaseClient
{
    private static readonly TimeSpan MetadataRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repository;

    public GitHubReleaseClient(
        HttpClient? httpClient = null,
        string owner = "Robertg761",
        string repository = "Pop")
    {
        // The overall HttpClient timeout would abort large downloads on slow links; timeouts
        // are enforced per request (metadata) and via a stall detector (downloads) instead.
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _owner = owner;
        _repository = repository;
    }

    public async Task<AppRelease?> FetchLatestLinuxReleaseAsync(CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(MetadataRequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_owner}/{_repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("PopLinuxApp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        HttpResponseMessage? response = null;
        try
        {
            try
            {
                response = await _httpClient.SendAsync(request, timeoutCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("timed out contacting GitHub. Check your connection and try again.");
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                throw new InvalidOperationException("GitHub rate limit exceeded. Try again later.");
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCancellation.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCancellation.Token);

            var root = document.RootElement;
            var releaseTag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var version = NormalizeReleaseVersion(releaseTag);
            if (!AppVersion.TryParse(version, out _))
            {
                throw new InvalidOperationException($"Latest release has an invalid version: {releaseTag}");
            }

            var expectedAssetName = $"Pop-linux-x64-{version}.AppImage";
            var checksumAssetName = $"{expectedAssetName}.sha256";
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                // The AppImage is uploaded by a separate workflow; a release without it just
                // means there is nothing for this platform to update to yet.
                return null;
            }

            Uri? assetUri = null;
            Uri? checksumUri = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;

                var downloadUrl = asset.TryGetProperty("browser_download_url", out var downloadUrlElement)
                    ? downloadUrlElement.GetString()
                    : null;

                if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                    || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(name, expectedAssetName, StringComparison.Ordinal))
                {
                    assetUri = uri;
                }
                else if (string.Equals(name, checksumAssetName, StringComparison.Ordinal))
                {
                    checksumUri = uri;
                }
            }

            return assetUri is null
                ? null
                : new AppRelease(version, expectedAssetName, assetUri, checksumUri);
        }
        finally
        {
            response?.Dispose();
        }
    }

    public async Task DownloadReleaseAsync(
        AppRelease release,
        string destinationPath,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        try
        {
            await DownloadAssetAsync(release.AssetUri, destinationPath, progress, cancellationToken);
            await VerifyChecksumAsync(release, destinationPath, cancellationToken);
        }
        catch
        {
            TryDeleteFile(destinationPath);
            throw;
        }
    }

    private async Task DownloadAssetAsync(
        Uri assetUri,
        string destinationPath,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        // The stall detector cancels the transfer if no bytes arrive for a while, without
        // capping how long a healthy download of a large file may take overall.
        using var stallCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var effectiveToken = stallCancellation.Token;

        using var request = new HttpRequestMessage(HttpMethod.Get, assetUri);
        request.Headers.UserAgent.ParseAdd("PopLinuxApp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        try
        {
            stallCancellation.CancelAfter(DownloadStallTimeout);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, effectiveToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(effectiveToken);
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var totalBytes = response.Content.Headers.ContentLength;
            var buffer = new byte[128 * 1024];
            long totalRead = 0;
            var lastProgress = -1;

            while (true)
            {
                stallCancellation.CancelAfter(DownloadStallTimeout);
                var read = await source.ReadAsync(buffer, effectiveToken);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), effectiveToken);
                totalRead += read;

                if (totalBytes is > 0)
                {
                    var percent = (int)Math.Round(totalRead / (double)totalBytes.Value * 100d);
                    var clamped = Math.Clamp(percent, 0, 100);
                    if (clamped != lastProgress)
                    {
                        lastProgress = clamped;
                        progress(clamped);
                    }
                }
            }

            stallCancellation.CancelAfter(Timeout.InfiniteTimeSpan);

            if (lastProgress != 100)
            {
                progress(100);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("the download stalled. Check your connection and try again.");
        }
    }

    private async Task VerifyChecksumAsync(AppRelease release, string downloadedFilePath, CancellationToken cancellationToken)
    {
        if (release.ChecksumUri is null)
        {
            throw new InvalidOperationException(
                $"the release is missing the checksum file {release.AssetName}.sha256, so the download couldn't be verified.");
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(MetadataRequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, release.ChecksumUri);
        request.Headers.UserAgent.ParseAdd("PopLinuxApp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        string checksumContent;
        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutCancellation.Token);
            response.EnsureSuccessStatusCode();
            checksumContent = await response.Content.ReadAsStringAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("timed out downloading the update checksum. Try again later.");
        }

        var expectedHash = checksumContent
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (expectedHash is null || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("the update checksum file is invalid, so the download couldn't be verified.");
        }

        byte[] actualHash;
        await using (var fileStream = File.OpenRead(downloadedFilePath))
        {
            actualHash = await SHA256.HashDataAsync(fileStream, cancellationToken);
        }

        if (!string.Equals(Convert.ToHexString(actualHash), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("the downloaded update failed checksum verification and was discarded. Try again later.");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string NormalizeReleaseVersion(string version) =>
        version.Trim().TrimStart('v', 'V');
}
