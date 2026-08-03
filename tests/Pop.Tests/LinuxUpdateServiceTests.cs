using System.Net;
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
                }
              ]
            }
            """;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(body));
        var client = new GitHubReleaseClient(httpClient);

        var release = await client.FetchLatestLinuxReleaseAsync(CancellationToken.None);

        Assert.Equal("1.4.2", release.Version);
        Assert.Equal("Pop-linux-x64-1.4.2.AppImage", release.AssetName);
        Assert.Equal("https://example.com/Pop-linux-x64-1.4.2.AppImage", release.AssetUri.ToString());
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

    private sealed class StubHttpMessageHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeUpdateClient : IUpdateClient
    {
        public string CurrentVersion { get; init; } = "1.0.0";

        public bool IsSupported { get; init; } = true;

        public string UnsupportedReason { get; init; } = "Unsupported";

        public string? PendingRestartVersion { get; init; }

        public UpdateDownloadResult Result { get; init; } = new(UpdateDownloadOutcome.NoUpdate, "Pop is up to date.");

        public IReadOnlyList<UpdateDownloadProgress> ProgressUpdates { get; init; } = [];

        public int PrepareCalls { get; private set; }

        public Task<UpdateDownloadResult> CheckForUpdatesAndDownloadAsync(Action<UpdateDownloadProgress> progress, CancellationToken cancellationToken)
        {
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
