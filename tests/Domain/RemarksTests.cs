using System.Diagnostics;

namespace Snail.MCP.Figma.Tests.Domain;

/// <summary>The queue behind the panel: reading takes the words, and a build answers only for what was typed while it ran.</summary>
public class RemarksTests
{
    private static BuildInterruption Left(DateTimeOffset moment, string? words = null) => new(words, [], moment);

    [Fact]
    public void ReadingTakesEverythingAndLeavesNothingBehind()
    {
        var remarks = new Remarks(10);
        var now = DateTimeOffset.UtcNow;
        remarks.Add(Left(now, "первое"));
        remarks.Add(Left(now.AddSeconds(1), "второе"));

        var taken = remarks.TakeAll();

        Assert.Equal(["первое", "второе"], taken.Select(remark => remark.Remark));
        Assert.Equal(0, remarks.Count);
        Assert.Empty(remarks.TakeAll());
    }

    /// <summary>The whole point of the timestamp: a remark about the previous result must not stop the build that just started.</summary>
    [Fact]
    public void ABuildTakesOnlyTheRemarksLeftAfterItStarted()
    {
        var remarks = new Remarks(10);
        var started = DateTimeOffset.UtcNow;
        remarks.Add(Left(started.AddSeconds(-5), "про прошлую сборку"));
        remarks.Add(Left(started.AddSeconds(2), "стой, тут дефект"));

        var stepIn = remarks.TakeSince(started);

        Assert.Equal("стой, тут дефект", stepIn?.Remark);
        Assert.Equal(1, remarks.Count);
        Assert.Null(remarks.TakeSince(started));
        Assert.Equal("про прошлую сборку", Assert.Single(remarks.TakeAll()).Remark);
    }

    /// <summary>Nothing in MCP wakes an agent, so this wait is what makes a remark arrive in a second instead of at the agent's next turn.</summary>
    [Fact]
    public async Task TheWaitEndsTheMomentSomebodyWrites()
    {
        var remarks = new Remarks(10);
        var started = Stopwatch.StartNew();
        var waiting = remarks.TryWaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        _ = Task.Run(async () =>
        {
            await Task.Delay(60);
            remarks.Add(Left(DateTimeOffset.UtcNow, "нашёл дефект"));
        });

        Assert.True(await waiting);
        Assert.True(started.ElapsedMilliseconds < 2000, $"the wait took {started.ElapsedMilliseconds} ms instead of ending on arrival");
    }

    [Fact]
    public async Task TheWaitGivesUpWhenNobodyWrites()
    {
        var remarks = new Remarks(10);

        Assert.False(await remarks.TryWaitAsync(TimeSpan.FromMilliseconds(80), CancellationToken.None));
    }

    [Fact]
    public async Task WaitingIsOverBeforeItStartsWhenARemarkIsAlreadyThere()
    {
        var remarks = new Remarks(10);
        remarks.Add(Left(DateTimeOffset.UtcNow, "уже лежит"));

        Assert.True(await remarks.TryWaitAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None));
    }

    [Fact]
    public void TheOldestRemarkIsDroppedOnceTheCapIsReached()
    {
        var remarks = new Remarks(2);
        var now = DateTimeOffset.UtcNow;
        remarks.Add(Left(now, "первое"));
        remarks.Add(Left(now.AddSeconds(1), "второе"));
        remarks.Add(Left(now.AddSeconds(2), "третье"));

        Assert.Equal(["второе", "третье"], remarks.TakeAll().Select(remark => remark.Remark));
    }
}
