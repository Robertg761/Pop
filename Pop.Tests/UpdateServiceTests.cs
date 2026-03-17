using Pop.App.Services;

namespace Pop.Tests;

public sealed class UpdateServiceTests
{
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
                new UpdateDownloadProgress("1.1.0", 25),
                new UpdateDownloadProgress("1.1.0", 100)
            ]
        };
        using var service = new UpdateService(client, new FakeShutdownHandler(), TimeSpan.FromDays(1), TimeSpan.FromDays(1));
        var states = new List<UpdateState>();
        service.StateChanged += (_, args) => states.Add(args.State);

        await service.CheckNowAsync();

        Assert.Contains(states, state => state.Status == UpdateStatus.Checking);
        Assert.Contains(states, state => state.Status == UpdateStatus.Downloading && state.DownloadProgressPercent == 25);
        Assert.Equal(UpdateStatus.ReadyToInstall, service.CurrentState.Status);
        Assert.Equal("1.1.0", service.CurrentState.AvailableVersion);
        Assert.True(service.CurrentState.CanInstall);
    }

    [Fact]
    public async Task StartAsync_RunsScheduledCheckAfterInitialDelay()
    {
        var client = new FakeUpdateClient
        {
            Result = new UpdateDownloadResult(UpdateDownloadOutcome.NoUpdate, "You're up to date.")
        };
        using var service = new UpdateService(client, new FakeShutdownHandler(), TimeSpan.FromMilliseconds(10), TimeSpan.FromDays(1));

        await service.StartAsync();
        await client.Checked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, client.CheckCalls);
        Assert.Equal(UpdateStatus.UpToDate, service.CurrentState.Status);
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
    public async Task CheckNowAsync_UnsupportedClientLeavesUnsupportedState()
    {
        var client = new FakeUpdateClient
        {
            IsSupported = false,
            UnsupportedReason = "Install Pop from an official GitHub release to enable in-app updates."
        };
        using var service = new UpdateService(client, new FakeShutdownHandler(), TimeSpan.FromDays(1), TimeSpan.FromDays(1));

        await service.CheckNowAsync();

        Assert.Equal(UpdateStatus.Unsupported, service.CurrentState.Status);
        Assert.False(service.CurrentState.CanCheck);
    }

    private sealed class FakeUpdateClient : IUpdateClient
    {
        public string CurrentVersion { get; init; } = "1.0.0";

        public bool IsSupported { get; init; } = true;

        public string UnsupportedReason { get; init; } = "Unsupported";

        public string? PendingRestartVersion { get; init; }

        public UpdateDownloadResult Result { get; init; } = new(UpdateDownloadOutcome.NoUpdate, "You're up to date.");

        public IReadOnlyList<UpdateDownloadProgress> ProgressUpdates { get; init; } = [];

        public int CheckCalls { get; private set; }

        public int PrepareCalls { get; private set; }

        public TaskCompletionSource<bool> Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<UpdateDownloadResult> CheckForUpdatesAndDownloadAsync(Action<UpdateDownloadProgress> progress, CancellationToken cancellationToken)
        {
            CheckCalls++;
            Checked.TrySetResult(true);

            if (!IsSupported)
            {
                return Task.FromResult(new UpdateDownloadResult(UpdateDownloadOutcome.Unsupported, UnsupportedReason));
            }

            foreach (var update in ProgressUpdates)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
