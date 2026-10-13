using Snail.MCP.Figma.Adapters.Editor;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>The turn of one agent's commands in one file: a few reads together, a write alone, in the order they came.</summary>
public class CommandLaneTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ReadsGoTogetherUpToTheLimitAndTheNextOneWaits()
    {
        var lane = new CommandLane();
        var reads = new List<IDisposable>();

        for (var i = 0; i < CommandLane.ReadsAtOnce; i++)
        {
            reads.Add(await lane.EnterAsync(CommandAccess.Read, CancellationToken.None));
        }

        var next = lane.EnterAsync(CommandAccess.Read, CancellationToken.None);
        await Task.Delay(Settle);
        Assert.False(next.IsCompleted);

        reads[0].Dispose();

        (await next).Dispose();
    }

    [Fact]
    public async Task AWriteWaitsForTheReadsInFlight()
    {
        var lane = new CommandLane();
        var read = await lane.EnterAsync(CommandAccess.Read, CancellationToken.None);

        var write = lane.EnterAsync(CommandAccess.Write, CancellationToken.None);
        await Task.Delay(Settle);
        Assert.False(write.IsCompleted);

        read.Dispose();

        (await write).Dispose();
    }

    /// <summary>A read sent after a write must see it, so it does not slip past the write while the write waits.</summary>
    [Fact]
    public async Task AReadThatComesAfterAWaitingWriteGoesAfterIt()
    {
        var lane = new CommandLane();
        var read = await lane.EnterAsync(CommandAccess.Read, CancellationToken.None);
        var write = lane.EnterAsync(CommandAccess.Write, CancellationToken.None);
        await Task.Delay(Settle);

        var later = lane.EnterAsync(CommandAccess.Read, CancellationToken.None);
        await Task.Delay(Settle);
        Assert.False(later.IsCompleted);

        read.Dispose();
        var writing = await write;
        await Task.Delay(Settle);
        Assert.False(later.IsCompleted);

        writing.Dispose();
        (await later).Dispose();
    }

    /// <summary>A write that gave up while waiting returns what it had taken, or the lane would stay narrower for good.</summary>
    [Fact]
    public async Task AWriteThatGivesUpLeavesTheLaneWhole()
    {
        var lane = new CommandLane();
        var read = await lane.EnterAsync(CommandAccess.Read, CancellationToken.None);
        using var giveUp = new CancellationTokenSource(Settle);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lane.EnterAsync(CommandAccess.Write, giveUp.Token));
        read.Dispose();

        var reads = new List<IDisposable>();

        for (var i = 0; i < CommandLane.ReadsAtOnce; i++)
        {
            reads.Add(await lane.EnterAsync(CommandAccess.Read, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)));
        }

        reads.ForEach(turn => turn.Dispose());
        (await lane.EnterAsync(CommandAccess.Write, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2))).Dispose();
    }

    [Fact]
    public async Task EndingATurnTwiceEndsItOnce()
    {
        var lane = new CommandLane();
        var turn = await lane.EnterAsync(CommandAccess.Write, CancellationToken.None);

        turn.Dispose();
        turn.Dispose();

        var reads = new List<IDisposable>();

        for (var i = 0; i < CommandLane.ReadsAtOnce; i++)
        {
            reads.Add(await lane.EnterAsync(CommandAccess.Read, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1)));
        }

        var extra = lane.EnterAsync(CommandAccess.Read, CancellationToken.None);
        await Task.Delay(Settle);
        Assert.False(extra.IsCompleted);

        reads.ForEach(read => read.Dispose());
        (await extra).Dispose();
    }
}
