using System.Diagnostics;
using Snail.MCP.Figma.Adapters.Persistence.Sqlite;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;
using Xunit.Abstractions;

namespace Snail.MCP.Figma.Tests.Chaos;

/// <summary>A large DOCUMENT_CHANGE — a paste, an undo of a big edit — invalidated on the hub's receive loop, which stops reading every agent's replies while it runs.</summary>
[Collection(nameof(SnapshotInvalidationChaosTests))]
public class SnapshotInvalidationChaosTests(SnapshotDatabaseFixture cacheFixture, ITestOutputHelper output) : IClassFixture<SnapshotDatabaseFixture>
{
    /// <summary>The receive loop of one file serves every agent working there; a reply held up longer than this is felt.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(100);

    private readonly SnapshotDatabaseFixture _cacheFixture = cacheFixture;

    private readonly string _fileKey = $"chaos-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Breaks the receive loop's latency: InvalidateNodesAsync deletes node by node, each DELETE its own WAL transaction, so two thousand changed ids are two thousand commits.</summary>
    /// <remarks>The best of three attempts is measured: a machine shared with other work stalls one attempt now and then, while a commit per node is slow in every one of them.</remarks>
    [Fact]
    public async Task SqliteNodeSnapshotStore_TwoThousandChangedNodes_InvalidatesWithinBudget()
    {
        var cacheOptions = new CacheOptions { Enabled = true, TtlHours = 1 };
        var cache = new SqliteNodeSnapshotStore(_cacheFixture.Database, cacheOptions, NullLogger<SqliteNodeSnapshotStore>.Instance);
        var changed = Enumerable.Range(1, 2000).Select(index => $"1:{index}").ToArray();

        await cache.InvalidateNodesAsync($"{_fileKey}-warm-up", ["1:1"]);
        var fastest = TimeSpan.MaxValue;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            foreach (var nodeId in changed)
            {
                await cache.SetAsync(Slice(nodeId));
            }

            var clock = Stopwatch.StartNew();
            var removed = await cache.InvalidateNodesAsync(_fileKey, changed);
            clock.Stop();

            Assert.Equal(changed.Length, removed);
            fastest = clock.Elapsed < fastest ? clock.Elapsed : fastest;
        }

        output.WriteLine($"2000 snapshots invalidated in {fastest.TotalMilliseconds:0} ms at best of three");
        Assert.True(fastest < Budget, $"{fastest.TotalMilliseconds:0} ms on the receive loop");
    }

    private NodeSnapshot Slice(string nodeId) => new()
    {
        FileKey = _fileKey,
        NodeId = nodeId,
        Variant = "chaos",
        FileVersion = "v1",
        OptimizedJson = "{}",
    };
}

/// <summary>A measured budget means nothing on a machine the other tests are loading: this collection runs alone.</summary>
[CollectionDefinition(nameof(SnapshotInvalidationChaosTests), DisableParallelization = true)]
public sealed class SnapshotInvalidationTimingCollection;
