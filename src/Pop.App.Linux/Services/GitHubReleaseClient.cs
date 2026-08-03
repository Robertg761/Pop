using System.Net.Http.Headers;
using System.Text.Json;

namespace Pop.App.Linux.Services;

internal sealed record AppRelease(string Version, string AssetName, Uri AssetUri);

internal sealed class GitHubReleaseClient
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repository;

    public GitHubReleaseClient(
        HttpClient? httpClient = null,
        string owner = "Robertg761",
        string repository = "Pop")
    {
        _httpClient = httpClient ?? new HttpClient();
        _owner = owner;
        _repository = repository;
    }

    public async Task<AppRelease> FetchLatestLinuxReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_owner}/{_repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("PopLinuxApp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        var releaseTag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var version = NormalizeReleaseVersion(releaseTag);
        if (!AppVersion.TryParse(version, out _))
        {
            throw new InvalidOperationException($"Latest release has an invalid version: {releaseTag}");
        }

        var expectedAssetName = $"Pop-linux-x64-{version}.AppImage";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Latest release does not include {expectedAssetName}.");
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            if (!string.Equals(name, expectedAssetName, StringComparison.Ordinal))
            {
                continue;
            }

            var downloadUrl = asset.TryGetProperty("browser_download_url", out var downloadUrlElement)
                ? downloadUrlElement.GetString()
                : null;

            if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var assetUri))
            {
                return new AppRelease(version, expectedAssetName, assetUri);
            }
        }

        throw new InvalidOperationException($"Latest release does not include {expectedAssetName}.");
    }

    public async Task DownloadReleaseAsync(
        AppRelease release,
        string destinationPath,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, release.AssetUri);
        request.Headers.UserAgent.ParseAdd("PopLinuxApp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var totalBytes = response.Content.Headers.ContentLength;
        var buffer = new byte[128 * 1024];
        long totalRead = 0;
        var lastProgress = -1;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
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

        if (lastProgress != 100)
        {
            progress(100);
        }
    }

    private static string NormalizeReleaseVersion(string version) =>
        version.Trim().TrimStart('v', 'V');
}
