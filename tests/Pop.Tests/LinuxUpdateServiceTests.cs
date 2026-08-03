using System.Net;
using System.Security.Cryptography;
using Pop.App.Linux.Services;

namespace Pop.Tests;

public sealed class LinuxUpdateServiceTests
{
    [Fact]
    public void AppVersion_ParsesPrefixedAndMetadataVersions()
    {
        Assert.True(AppVersion.TryParse("v1.2.3+build.4", out var current));
        Assert.True(AppVersion.TryParse("1.2.4", out var next));

        Assert.True(next > current);
    }

    [Fact]
    public void AppVersion_TreatsPreReleaseAsLowerThanSameNumberedRelease()
    {
        Assert.True(AppVersion.TryParse("0.4.0-beta", out var beta));
        Assert.True(AppVersion.TryParse("0.4.0", out var release));

        Assert.True(release > beta);
        Assert.True(beta < release);
        Assert.False(beta.Equals(release));
    }

    [Fact]
    public void AppVersion_OrdersPreReleaseIdentifiersLikeSemver()
    {
        Assert.True(AppVersion.TryParse("0.4.0-alpha", out var alpha));
        Assert.True(AppVersion.TryParse("0.4.0-beta.2", out var beta2));
        Assert.True(AppVersion.TryParse("0.4.0-beta.10", out var beta10));
        Assert.True(AppVersion.TryParse("0.4.1-alpha", out var nextAlpha));
        Assert.True(AppVersion.TryParse("0.4.0", out var release));

        Assert.True(beta2 > alpha);
        Assert.True(beta10 > beta2);
        Assert.True(release > beta10);
        Assert.True(nextAlpha > release);
    }

    [Fact]
    public async Task GitHubReleaseClient_FindsMatchingLinuxAppImageAsset()
    {
        const string body = """
            {
              "tag_name": "v1.4.2",
              "assets": [
                {
                  "name": "Pop-linux-x64-1.4.2.tar.gz",
                  "browser_download_url": "https://example.com/Pop-linux-x64-1.4.2.tar.gz"
                },
                {
                  "name": "Pop-linux-x64-1.4.2.AppImage",
                  "browser_download_url": "https://example.com/Pop-linux-x64-1.4.2.AppImage"
                },
                {
                  "name": "Pop-linux-x64-1.4.2.AppImage.sha256",
                  "browser_download_url": "https://example.com/Pop-linux-x64-1.4.2.AppImage.sha256"
                }
              ]
            }
            """;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(body));
        var client = new GitHubReleaseClient(httpClient);

        var release = await client.FetchLatestLinuxReleaseAsync(CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("1.4.2", release!.Version);
        Assert.Equal("Pop-linux-x64-1.4.2.AppImage", release.AssetName);
        Assert.Equal("https://example.com/Pop-linux-x64-1.4.2.AppImage", release.AssetUri.ToString());
        Assert.Equal("https://example.com/Pop-linux-x64-1.4.2.AppImage.sha256", release.ChecksumUri?.ToString());
    }

    [Fact]
    public async Task GitHubReleaseClient_MissingAppImageAssetIsTreatedAsNoRelease()
    {
        const string body = """
            {
              "tag_name": "v1.4.2",
              "assets": [
                {
                  "name": "Pop-win-x64-1.4.2-Setup.exe",
                  "browser_download_url": "https://example.com/Pop-win-x64-1.4.2-Setup.exe"
                }
              ]
            }
            """;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(body));
        var client = new GitHubReleaseClient(httpClient);

        var release = await client.FetchLatestLinuxReleaseAsync(CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task GitHubReleaseClient_RejectsNonHttpsAssetUrls()
    {
        const string body = """
            {
              "tag_name": "v1.4.2",
              "assets": [
                {
                  "name": "Pop-linux-x64-1.4.2.AppImage",
                  "browser_download_url": "http://example.com/Pop-linux-x64-1.4.2.AppImage"
                }
              ]
            }
            """;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(body));
        var client = new GitHubReleaseClient(httpClient);

        var release = await client.FetchLatestLinuxReleaseAsync(CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task GitHubReleaseClient_SurfacesFriendlyRateLimitError()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler("rate limited", HttpStatusCode.Forbidden));
        var client = new GitHubReleaseClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchLatestLinuxReleaseAsync(CancellationToken.None));

        Assert.Contains("rate limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHubReleaseClient_RejectsChecksumMismatchAndDeletesDownload()
    {
        var assetBytes = "new-appimage-bytes"u8.ToArray();
        var release = new AppRelease(
            "1.4.2",
            "Pop-linux-x64-1.4.2.AppImage",
            new Uri("https://example.com/Pop-linux-x64-1.4.2.AppImage"),
            new Uri("https://example.com/Pop-linux-x64-1.4.2.AppImage.sha256"));
        using var httpClient = new HttpClient(new RoutingHttpMessageHandler(request =>
            request.RequestUri == release.ChecksumUri
                ? CreateTextResponse($"{new string('0', 64)}  Pop-linux-x64-1.4.2.AppImage\n")
                : CreateBytesResponse(assetBytes)));
        var client = new GitHubReleaseClient(httpClient);
        var destinationPath = CreateTempFilePath();

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.DownloadReleaseAsync(release, destinationPath, _ => { }, CancellationToken.None));

            Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(destinationPath));
        }
        finally
        {
            CleanUpTempFile(destinationPath);
        }
    }

    [Fact]
    public async Task GitHubReleaseClient_FailsDownloadWhenChecksumAssetIsMissing()
    {
        var assetBytes = "new-appimage-bytes"u8.ToArray();
        var release = new AppRelease(
            "1.4.2",
            "Pop-linux-x64-1.4.2.AppImage",
            new Uri("https://example.com/Pop-linux-x64-1.4.2.AppImage"),
            ChecksumUri: null);
        using var httpClient = new HttpClient(new RoutingHttpMessageHandler(_ => CreateBytesResponse(assetBytes)));
        var client = new GitHubReleaseClient(httpClient);
        var destinationPath = CreateTempFilePath();

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.DownloadReleaseAsync(release, destinationPath, _ => { }, CancellationToken.None));

            Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(destinationPath));
        }
        finally
        {
            CleanUpTempFile(destinationPath);
        }
    }

    [Fact]
    public async Task GitHubReleaseClient_AcceptsDownloadWithMatchingChecksum()
    {
        var assetBytes = "new-appimage-bytes"u8.ToArray();
        var expectedHash = Convert.ToHexString(SHA256.HashData(assetBytes)).ToLowerInvariant();
        var release = new AppRelease(
            "1.4.2",
            "Pop-linux-x64-1.4.2.AppImage",
            new Uri("https://example.com/Pop-linux-x64-1.4.2.AppImage"),
            new Uri("https://example.com/Pop-linux-x64-1.4.2.AppImage.sha256"));
        using var httpClient = new HttpClient(new RoutingHttpMessageHandler(request =>
            request.RequestUri == release.ChecksumUri
                ? CreateTextResponse($"{expectedHash}  Pop-linux-x64-1.4.2.AppImage\n")
                : CreateBytesResponse(assetBytes)));
        var client = new GitHubReleaseClient(httpClient);
        var destinationPath = CreateTempFilePath();

        try
        {
            await client.DownloadReleaseAsync(release, destinationPath, _ => { }, CancellationToken.None);

            Assert.Equal(assetBytes, await File.ReadAllBytesAsync(destinationPath));
        }
        finally
        {
            CleanUpTempFile(destinationPath);
        }
    }

    [Fact]
    public async Task CheckNowAsync_PublishesReadyToInstallAfterDownloadCompletes()
    {
        var client = new FakeUpdateClient
        {
            Result = new UpdateDownloadResult(
                UpdateDownloadOutcome.ReadyToInstall,
                "Update v1.1.0 is ready to install.",
                "1.1.0"),
            ProgressUpdates =
            [
                new UpdateDownloadProgress("1.1.0", 40),
                new UpdateDownloadProgress("1.1.0", 100)
            ]
        };
        using var service = new UpdateService(client, new FakeShutdownHandler(), TimeSpan.FromDays(1), TimeSpan.FromDays(1));
        var states = new List<UpdateState>();
        service.StateChanged += (_, args) => states.Add(args.State);

        await service.CheckNowAsync();

        Assert.Contains(states, state => state.Status == UpdateStatus.Checking);
        Assert.Contains(states, state => state.Status == UpdateStatus.Downloading && state.DownloadProgressPercent == 40);
        Assert.Equal(UpdateStatus.ReadyToInstall, service.CurrentState.Status);
        Assert.Equal("1.1.0", service.CurrentState.AvailableVersion);
    }

    [Fact]
    public void ApplyPendingUpdateAndRestart_ShutsDownWhenPendingUpdateIsPrepared()
    {
        var client = new FakeUpdateClient
        {
            PendingRestartVersion = "1.1.0"
        };
        var shutdown = new FakeShutdownHandler();
        using var service = new UpdateService(client, shutdown, TimeSpan.FromDays(1), TimeSpan.FromDays(1));

        service.ApplyPendingUpdateAndRestart();

        Assert.Equal(1, client.PrepareCalls);
        Assert.Equal(1, shutdown.ShutdownCalls);
    }

    [Fact]
    public void ApplyPendingUpdateAndRestart_PublishesErrorWhenPendingUpdateUnavailable()
    {
        var client = new FakeUpdateClient();
        var shutdown = new FakeShutdownHandler();
        using var service = new UpdateService(client, shutdown, TimeSpan.FromDays(1), TimeSpan.FromDays(1));

        service.ApplyPendingUpdateAndRestart();

        Assert.Equal(0, shutdown.ShutdownCalls);
        Assert.Equal(UpdateStatus.Error, service.CurrentState.Status);
        Assert.True(service.CurrentState.CanCheck);
        Assert.Contains("no longer available", service.CurrentState.Message);
    }

    [Fact]
    public async Task CheckNowAsync_CancelledCheckRestoresTerminalState()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FakeUpdateClient
        {
            CheckOverride = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new UpdateDownloadResult(UpdateDownloadOutcome.NoUpdate, "unused"));
            }
        };
        using var service = new UpdateService(client, new FakeShutdownHandler(), TimeSpan.FromDays(1), TimeSpan.FromDays(1));

        await service.CheckNowAsync(cancellation.Token);

        Assert.Equal(UpdateStatus.Idle, service.CurrentState.Status);
        Assert.True(service.CurrentState.CanCheck);
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndCheckNowAfterDisposeIsNoOp()
    {
        var client = new FakeUpdateClient();
        var service = new UpdateService(client, new FakeShutdownHandler(), TimeSpan.FromDays(1), TimeSpan.FromDays(1));

        service.Dispose();
        service.Dispose();

        await service.CheckNowAsync();

        Assert.Equal(0, client.CheckCalls);
    }

    [Fact]
    public void LoadPreparedUpdate_CorruptMetadataIsDiscardedWithStagingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pop-linux-update-test-{Guid.NewGuid():N}");
        try
        {
            var baseDirectory = Path.Combine(root, "Updates");
            var stagingDirectory = Path.Combine(baseDirectory, "1.2.3");
            var metadataPath = Path.Combine(baseDirectory, "prepared-update.json");
            Directory.CreateDirectory(stagingDirectory);
            File.WriteAllText(Path.Combine(stagingDirectory, "Pop-linux-x64-1.2.3.AppImage"), "staged");
            File.WriteAllText(metadataPath, "{ this is not valid json");

            var installer = new PreparedAppImageUpdateInstaller(baseDirectory, Path.Combine(root, "Pop.AppImage"));

            Assert.Null(installer.LoadPreparedUpdate());
            Assert.False(File.Exists(metadataPath));
            Assert.False(Directory.Exists(stagingDirectory));
            Assert.Null(installer.LoadPreparedUpdate());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CleanUpAbandonedDownloads_RemovesLeftoverPartialDownloads()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pop-linux-update-test-{Guid.NewGuid():N}");
        try
        {
            var baseDirectory = Path.Combine(root, "Updates");
            var downloadsDirectory = Path.Combine(baseDirectory, "Downloads");
            Directory.CreateDirectory(downloadsDirectory);
            var leftoverPath = Path.Combine(downloadsDirectory, $"Pop-linux-x64-1.2.3-{Guid.NewGuid():N}.AppImage");
            File.WriteAllText(leftoverPath, "partial");

            var installer = new PreparedAppImageUpdateInstaller(baseDirectory, Path.Combine(root, "Pop.AppImage"));
            installer.CleanUpAbandonedDownloads();

            Assert.False(File.Exists(leftoverPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AppImageUpdateClient_MissingAppImageAssetReportsUpToDate()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"pop-linux-update-test-{Guid.NewGuid():N}");
        try
        {
            var targetPath = Path.Combine(root, "Pop.AppImage");
            Directory.CreateDirectory(root);
            File.WriteAllText(targetPath, "current");

            const string body = """
                {
                  "tag_name": "v9.9.9",
                  "assets": []
                }
                """;
            using var httpClient = new HttpClient(new StubHttpMessageHandler(body));
            var client = new AppImageUpdateClient(
                new GitHubReleaseClient(httpClient),
                new PreparedAppImageUpdateInstaller(Path.Combine(root, "Updates"), targetPath));

            var result = await client.CheckForUpdatesAndDownloadAsync(_ => { }, CancellationToken.None);

            Assert.Equal(UpdateDownloadOutcome.NoUpdate, result.Outcome);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void PreparedAppImageUpdateInstaller_PreparesAndLoadsStagedUpdate()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"pop-linux-update-test-{Guid.NewGuid():N}");
        try
        {
            var targetPath = Path.Combine(root, "Pop.AppImage");
            var downloadPath = Path.Combine(root, "download.AppImage");
            Directory.CreateDirectory(root);
            File.WriteAllText(targetPath, "current");
            File.WriteAllText(downloadPath, "next");

            var installer = new PreparedAppImageUpdateInstaller(
                Path.Combine(root, "Updates"),
                targetPath);

            Assert.True(installer.IsSupportedInstallation);

            var prepared = installer.PrepareUpdate(downloadPath, "1.2.3");
            var loaded = installer.LoadPreparedUpdate();

            Assert.Equal("1.2.3", prepared.Version);
            Assert.NotNull(loaded);
            Assert.Equal(prepared.StagedAppImagePath, loaded!.StagedAppImagePath);
            Assert.True(File.Exists(loaded.StagedAppImagePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateTempFilePath() =>
        Path.Combine(Path.GetTempPath(), $"pop-linux-update-test-{Guid.NewGuid():N}", "download.AppImage");

    private static void CleanUpTempFile(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HttpResponseMessage CreateTextResponse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage CreateBytesResponse(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private sealed class StubHttpMessageHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class RoutingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class FakeUpdateClient : IUpdateClient
    {
        public string CurrentVersion { get; init; } = "1.0.0";

        public bool IsSupported { get; init; } = true;

        public string UnsupportedReason { get; init; } = "Unsupported";

        public string? PendingRestartVersion { get; init; }

        public UpdateDownloadResult Result { get; init; } = new(UpdateDownloadOutcome.NoUpdate, "Pop is up to date.");

        public IReadOnlyList<UpdateDownloadProgress> ProgressUpdates { get; init; } = [];

        public Func<CancellationToken, Task<UpdateDownloadResult>>? CheckOverride { get; init; }

        public int CheckCalls { get; private set; }

        public int PrepareCalls { get; private set; }

        public Task<UpdateDownloadResult> CheckForUpdatesAndDownloadAsync(Action<UpdateDownloadProgress> progress, CancellationToken cancellationToken)
        {
            CheckCalls++;

            if (CheckOverride is not null)
            {
                return CheckOverride(cancellationToken);
            }

            foreach (var update in ProgressUpdates)
            {
                progress(update);
            }

            return Task.FromResult(Result);
        }

        public bool PreparePendingUpdateAndRestart()
        {
            PrepareCalls++;
            return !string.IsNullOrWhiteSpace(PendingRestartVersion);
        }
    }

    private sealed class FakeShutdownHandler : IAppShutdownHandler
    {
        public int ShutdownCalls { get; private set; }

        public void RequestShutdown()
        {
            ShutdownCalls++;
        }
    }
}
