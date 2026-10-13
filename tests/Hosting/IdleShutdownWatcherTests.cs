using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Hosting;

namespace Snail.MCP.Figma.Tests.Hosting;

public class IdleShutdownWatcherTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMinutes(5);

    private static readonly IdleShutdownOptions Options = new(TimeSpan.FromMinutes(60), Poll);

    [Fact]
    public async Task ExitsWhenNothingHappensAndNoPluginIsConnected()
    {
        var (time, lifetime, _, watcher) = Build();

        await watcher.StartAsync(CancellationToken.None);
        await AdvanceAsync(time, TimeSpan.FromMinutes(65));

        Assert.True(lifetime.WasStopped);

        await watcher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaysAliveWhileThePluginIsConnected()
    {
        var (time, lifetime, documents, watcher) = Build();
        documents.HasConnection = true;

        await watcher.StartAsync(CancellationToken.None);
        await AdvanceAsync(time, TimeSpan.FromMinutes(100));

        Assert.False(lifetime.WasStopped);

        await watcher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RestartsTheCountdownAfterThePluginDisconnects()
    {
        var (time, lifetime, documents, watcher) = Build();
        documents.HasConnection = true;

        await watcher.StartAsync(CancellationToken.None);
        await AdvanceAsync(time, TimeSpan.FromMinutes(100));
        documents.HasConnection = false;

        await AdvanceAsync(time, TimeSpan.FromMinutes(50));
        Assert.False(lifetime.WasStopped);

        await AdvanceAsync(time, TimeSpan.FromMinutes(15));
        Assert.True(lifetime.WasStopped);

        await watcher.StopAsync(CancellationToken.None);
    }

    /// <summary>Advances one poll at a time, giving the watcher's loop a real moment to consume each tick.</summary>
    private static async Task AdvanceAsync(FakeTimeProvider time, TimeSpan total)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < total; elapsed += Poll)
        {
            time.Advance(Poll);
            await Task.Delay(15);
        }
    }

    private static (FakeTimeProvider Time, FakeLifetime Lifetime, FakeOpenDocuments Documents, IdleShutdownWatcher Watcher) Build()
    {
        var time = new FakeTimeProvider();
        var lifetime = new FakeLifetime();
        var documents = new FakeOpenDocuments();
        var watcher = new IdleShutdownWatcher(
            new ActivityTracker(time),
            documents,
            Options,
            lifetime,
            time,
            NullLogger<IdleShutdownWatcher>.Instance);

        return (time, lifetime, documents, watcher);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public bool WasStopped { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => WasStopped = true;
    }

    private sealed class FakeOpenDocuments : IOpenDocuments
    {
        public bool HasConnection { get; set; }

        public IReadOnlyCollection<IOpenDocument> Sessions => [];

        public string? ActiveFileKey => null;

        public IOpenDocument? LastTouched => null;

        public Task<bool> TryClaimAsync(string fileKey, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public IOpenDocument? Resolve(string? fileKey = null) => null;

        public Remarks RemarksIn(IOpenDocument document) => new(20);

        public Task PublishRemarksWaitingAsync(IOpenDocument document, CancellationToken cancellationToken) => Task.CompletedTask;

        public long ConsoleMark(IOpenDocument document) => 0;

        public IReadOnlyList<string> OthersIn(IOpenDocument document) => [];

        public void ClearConsole(IOpenDocument document)
        {
        }
    }
}
