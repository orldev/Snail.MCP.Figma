using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>Where each agent of a shared server worked, kept across a restart and believed only while it can still be right.</summary>
public sealed class AgentTargetsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("snail-agents-").FullName;

    private string File => Path.Combine(_directory, AgentRoster.AgentsFileName);

    private AgentTargets Targets() => new(File, NullLogger.Instance);

    private static bool IsNothingConnected(string fileKey) => false;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AnotherProcessReadsWhatThisOneWrote()
    {
        var now = DateTimeOffset.UtcNow;
        Targets().Remember("Keynex", "web", "Web", now);

        var recalled = Targets().Recall("Keynex", now.AddMinutes(1), IsNothingConnected);

        Assert.Equal(("web", "Web"), recalled);
    }

    /// <summary>A record from yesterday would send a new session into a file the human has long closed.</summary>
    [Fact]
    public void AnOldRecordIsNotBelievedUnlessItsFileIsHereNow()
    {
        var then = DateTimeOffset.UtcNow.AddHours(-5);
        Targets().Remember("Keynex", "web", "Web", then);

        Assert.Null(Targets().Recall("Keynex", DateTimeOffset.UtcNow, IsNothingConnected));
        Assert.Equal(("web", "Web"), Targets().Recall("Keynex", DateTimeOffset.UtcNow, fileKey => fileKey == "web"));
    }

    [Fact]
    public void AnAgentWithoutAFileHasNoRecord()
    {
        var now = DateTimeOffset.UtcNow;
        var targets = Targets();
        targets.Remember("Keynex", "web", "Web", now);

        targets.Remember("Keynex", null, null, now);

        Assert.Null(Targets().Recall("Keynex", now, IsNothingConnected));
    }

    [Fact]
    public void AFileThatCannotBeReadMeansNoRecordsRatherThanAFailure()
    {
        System.IO.File.WriteAllText(File, "{ not json");

        Assert.Null(Targets().Recall("Keynex", DateTimeOffset.UtcNow, IsNothingConnected));
    }

    /// <summary>The roster gives a returning agent its file as soon as the client names its folder.</summary>
    [Fact]
    public async Task AnAgentNamedAfterItsFolderGetsItsFileBack()
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
    public async Task AReconnectedWindowNamedWithANumberGetsItsFolderFileBack()
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
}
