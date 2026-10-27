using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>Where each agent of a shared server worked, kept across a restart and believed only while it can still be right.</summary>
public sealed class AgentTargetsTests : IDisposable
{
    private readonly string _directory = TestFolder.Create();

    private string File => Path.Combine(_directory, AgentRoster.AgentsFileName);

    private AgentTargets Targets() => new(File, NullLogger.Instance);

    private static bool IsNothingConnected(string fileKey) => false;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AgentTargets_RecordWrittenByAnotherProcess_IsRecalled()
    {
        var now = DateTimeOffset.UtcNow;
        Targets().Remember("Keynex", "web", "Web", now);

        var recalled = Targets().Recall("Keynex", now.AddMinutes(1), IsNothingConnected);

        Assert.Equal(("web", "Web"), recalled);
    }

    /// <summary>A record from yesterday would send a new session into a file the human has long closed.</summary>
    [Fact]
    public void AgentTargets_OldRecord_IsBelievedOnlyWhileItsFileIsConnected()
    {
        var then = DateTimeOffset.UtcNow.AddHours(-5);
        Targets().Remember("Keynex", "web", "Web", then);

        Assert.Null(Targets().Recall("Keynex", DateTimeOffset.UtcNow, IsNothingConnected));
        Assert.Equal(("web", "Web"), Targets().Recall("Keynex", DateTimeOffset.UtcNow, fileKey => fileKey == "web"));
    }

    [Fact]
    public void AgentTargets_AgentLeftWithoutAFile_HasNoRecord()
    {
        var now = DateTimeOffset.UtcNow;
        var targets = Targets();
        targets.Remember("Keynex", "web", "Web", now);

        targets.Remember("Keynex", null, null, now);

        Assert.Null(Targets().Recall("Keynex", now, IsNothingConnected));
    }

    [Fact]
    public void AgentTargets_UnreadableFile_MeansNoRecordsRatherThanAFailure()
    {
        System.IO.File.WriteAllText(File, "{ not json");

        Assert.Null(Targets().Recall("Keynex", DateTimeOffset.UtcNow, IsNothingConnected));
    }

    /// <summary>The roster gives a returning agent its file as soon as the client names its folder.</summary>
    [Fact]
    public async Task AgentRoster_AgentNamedAfterItsFolder_GetsItsFileBack()
    {
        Targets().Remember("Keynex", "web", "Web", DateTimeOffset.UtcNow);
        var registry = new OpenDocumentRegistry(NullLogger<OpenDocumentRegistry>.Instance);
        var roster = new AgentRoster(registry, new ServerConfig { Transport = Transport.Http, DataDirectory = _directory }, NullLogger<AgentRoster>.Instance);
        var returning = roster.Open("after-restart", "claude-code");

        await roster.NameAsync(returning, "Keynex");

        Assert.Equal("web", returning.ActiveFileKey);
    }

    /// <summary>A window that reconnects opens a new session while its old one has not timed out yet: it is named Keynex 2 for that while, and is still Keynex with Keynex's file.</summary>
    [Fact]
    public async Task AgentRoster_ReconnectedWindowNamedWithANumber_GetsItsFolderFileBack()
    {
        Targets().Remember("Keynex", "web", "Web", DateTimeOffset.UtcNow);
        var registry = new OpenDocumentRegistry(NullLogger<OpenDocumentRegistry>.Instance);
        var roster = new AgentRoster(registry, new ServerConfig { Transport = Transport.Http, DataDirectory = _directory }, NullLogger<AgentRoster>.Instance);
        var lingering = roster.Open("old-session", "claude-code");
        await roster.NameAsync(lingering, "Keynex");
        var reconnected = roster.Open("new-session", "claude-code");

        await roster.NameAsync(reconnected, "Keynex");

        Assert.Equal("Keynex 2", reconnected.Name);
        Assert.Equal("web", reconnected.ActiveFileKey);
    }

    /// <summary>A client opens with several calls at once: one asks for the folder, the others wait for that answer instead of going on unnamed.</summary>
    [Fact]
    public async Task AgentSlot_FirstCallsAtOnce_ShareOneNaming()
    {
        var registry = new OpenDocumentRegistry(NullLogger<OpenDocumentRegistry>.Instance);
        var roster = new AgentRoster(registry, new ServerConfig { Transport = Transport.Http, DataDirectory = _directory }, NullLogger<AgentRoster>.Instance);
        var agent = roster.Open("session", "claude-code");
        var answer = new TaskCompletionSource();
        var asked = 0;

        Task Naming()
        {
            Interlocked.Increment(ref asked);

            return answer.Task.ContinueWith(_ => roster.NameAsync(agent, "Keynex"), TaskScheduler.Default).Unwrap();
        }

        var first = agent.NameOnceAsync(Naming);
        var second = agent.NameOnceAsync(Naming);
        Assert.False(second.IsCompleted);

        answer.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, asked);
        Assert.Equal("Keynex", agent.Name);
    }

    /// <summary>A file chosen before the client named its folder is written down once the name arrives.</summary>
    [Fact]
    public async Task AgentRoster_FileChosenBeforeTheName_IsWrittenDownWithIt()
    {
        var registry = new OpenDocumentRegistry(NullLogger<OpenDocumentRegistry>.Instance);
        var roster = new AgentRoster(registry, new ServerConfig { Transport = Transport.Http, DataDirectory = _directory }, NullLogger<AgentRoster>.Instance);
        var agent = roster.Open("session", "claude-code");
        agent.Return("web", "Web");

        await roster.NameAsync(agent, "Keynex");

        Assert.Equal(("web", "Web"), Targets().Recall("Keynex", DateTimeOffset.UtcNow, IsNothingConnected));
    }
}
