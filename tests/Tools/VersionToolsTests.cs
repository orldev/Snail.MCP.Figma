using Snail.MCP.Figma.Application.Diff;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Version history tools over a fake REST: request cost and a pagination cursor that loses no versions.</summary>
[Collection(nameof(BridgeHostFixture))]
public class VersionToolsTests(BridgeHostFixture bridge)
{
    private const string FileUrl = "https://www.figma.com/design/ABC123XYZ/Design-System";

    private readonly BridgeHostFixture _bridge = bridge;

    private VersionTools Tools(ScriptedPublishedSource api) => new(
        _bridge.Gateway,
        api,
        api,
        new ActiveDocumentTracker(),
        new VersionSnapshotCache(),
        NullLogger<VersionTools>.Instance);

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private static JsonNode Data(string response)
    {
        var envelope = Parse(response);
        Assert.True(envelope["ok"]!.GetValue<bool>(), envelope["error"]?.GetValue<string>() ?? "expected success");

        return envelope["data"]!;
    }

    private static string[] Ids(JsonNode? versions) =>
        [.. (versions as JsonArray ?? []).Select(version => version!["id"]!.GetValue<string>())];

    /// <summary>History of n versions, newest first; every second one is an unlabelled autosave.</summary>
    private static ScriptedPublishedSource WithHistory(int count, bool everySecondIsAutosave = true)
    {
        var api = new ScriptedPublishedSource();

        for (var i = count; i >= 1; i--)
        {
            var labeled = !everySecondIsAutosave || i % 2 == 1;
            api.Versions.Add(ScriptedPublishedSource.Version($"v{i}", labeled ? $"Release {i}" : null));
        }

        return api;
    }

    [Fact]
    public async Task AutosavesAreSkippedByDefaultAndCounted()
    {
        var api = WithHistory(30);

        var data = Data(await Tools(api).GetFileVersionsAsync(FileUrl));

        Assert.Equal(15, data["summary"]!["returned"]!.GetValue<int>());
        Assert.Equal(15, data["summary"]!["autosaves_skipped"]!.GetValue<int>());
        Assert.False(data["pagination"]!["has_more"]!.GetValue<bool>());
        Assert.Null(data["pagination"]!["next_cursor"]);
        Assert.All(data["versions"]!.AsArray(), version => Assert.True(version!["is_labeled"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task CursorResumesExactlyAfterTheLastVersionHandedOut()
    {
        var api = WithHistory(30, everySecondIsAutosave: false);
        var tools = Tools(api);

        var first = Data(await tools.GetFileVersionsAsync(FileUrl, includeAutosaves: true, maxVersions: 10));
        var firstIds = Ids(first["versions"]);

        Assert.Equal(10, firstIds.Length);
        Assert.True(first["pagination"]!["has_more"]!.GetValue<bool>());

        var cursor = first["pagination"]!["next_cursor"]!.GetValue<string>();
        Assert.Equal(firstIds[^1], cursor);

        var second = Data(await tools.GetFileVersionsAsync(FileUrl, includeAutosaves: true, maxVersions: 10, cursor: cursor));
        var secondIds = Ids(second["versions"]);

        Assert.Equal(10, secondIds.Length);
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.Equal("v20", secondIds[0]);
    }

    [Fact]
    public async Task ListingWalksPagesUntilTheCapIsReached()
    {
        var api = WithHistory(120, everySecondIsAutosave: false);

        var data = Data(await Tools(api).GetFileVersionsAsync(FileUrl, includeAutosaves: true, maxVersions: 120));

        Assert.Equal(120, data["summary"]!["returned"]!.GetValue<int>());
        Assert.Equal(3, data["summary"]!["api_calls_made"]!.GetValue<int>());
        Assert.False(data["pagination"]!["has_more"]!.GetValue<bool>());
    }

    [Fact]
    public async Task EmptyHistoryComesBackAsAnEmptyListNotAsAnError()
    {
        var data = Data(await Tools(new ScriptedPublishedSource()).GetFileVersionsAsync(FileUrl));

        Assert.Empty(data["versions"]!.AsArray());
        Assert.False(data["pagination"]!["has_more"]!.GetValue<bool>());
    }

    [Fact]
    public async Task PastVersionIsFetchedOnceAndThenServedFromCacheWhileHeadNeverIs()
    {
        var api = WithHistory(4, everySecondIsAutosave: false);
        api.PutFile("v1", new JsonObject { ["id"] = "0:0", ["children"] = new JsonArray() });
        var tools = Tools(api);

        await tools.GetFileAtVersionAsync("v1", FileUrl);
        await tools.GetFileAtVersionAsync("v1", FileUrl);

        Assert.Equal(1, api.FileCalls);

        await tools.GetFileAtVersionAsync("current", FileUrl);
        await tools.GetFileAtVersionAsync("current", FileUrl);

        Assert.Equal(3, api.FileCalls);
    }

    [Fact]
    public async Task DiffOfPageStructureCostsExactlyTwoRequests()
    {
        var api = WithHistory(2, everySecondIsAutosave: false);
        api.PutFile("v1", new JsonObject
        {
            ["children"] = new JsonArray(new JsonObject { ["id"] = "0:1", ["name"] = "Cover" }),
        });
        api.PutFile("v2", new JsonObject
        {
            ["children"] = new JsonArray(
                new JsonObject { ["id"] = "0:1", ["name"] = "Cover v2" },
                new JsonObject { ["id"] = "0:2", ["name"] = "Icons" }),
        });

        var data = Data(await Tools(api).DiffVersionsAsync("v1", "v2", FileUrl));

        Assert.Equal(2, api.FileCalls);
        Assert.Equal(0, api.NodeCalls);
        Assert.Equal(1, data["page_structure"]!["summary"]!["added"]!.GetValue<int>());
        Assert.Equal(1, data["page_structure"]!["summary"]!["renamed"]!.GetValue<int>());
        Assert.Equal(0, data["summary"]!["scoped_node_count"]!.GetValue<int>());
    }

    [Fact]
    public async Task ScopedComponentsAddTwoNodeRequestsAndTheirOwnDelta()
    {
        var api = WithHistory(2, everySecondIsAutosave: false);
        api.PutNode("v1", "10:1", ScriptedPublishedSource.ComponentSet("10:1", "Button", ["Disabled#1:2"]));
        api.PutNode("v2", "10:1", ScriptedPublishedSource.ComponentSet("10:1", "Button v2", ["Disabled#1:2", "Size#1:3"]));

        var data = Data(await Tools(api).DiffVersionsAsync("v1", "v2", FileUrl, "10:1", DiffModes.Detailed));

        Assert.Equal(2, api.NodeCalls);
        Assert.Equal(1, data["summary"]!["scoped_node_count"]!.GetValue<int>());

        var node = data["scoped_nodes"]![0]!;
        Assert.Equal("Button v2", node["node_name"]!.GetValue<string>());
        Assert.Equal(1, node["component_properties"]!["summary"]!["added"]!.GetValue<int>());
        Assert.Equal(2, node["change_count"]!.GetValue<int>());
    }

    [Fact]
    public async Task ChangelogRendersMarkdownOnTopOfTheSameDiff()
    {
        var api = WithHistory(2, everySecondIsAutosave: false);
        api.PutNode("v1", "10:1", ScriptedPublishedSource.ComponentSet("10:1", "Button"));
        api.PutNode("v2", "10:1", ScriptedPublishedSource.ComponentSet("10:1", "Button v2"));

        var data = Data(await Tools(api).GenerateChangelogAsync("v1", "v2", FileUrl, "10:1"));
        var markdown = data["markdown"]!.GetValue<string>();

        Assert.Contains("changelog", markdown);
        Assert.Contains("'Release 1'", markdown);
        Assert.Contains("### Button v2 — `10:1`", markdown);
        Assert.NotNull(data["scoped_nodes"]);
    }

    /// <summary>History of five versions where the property exists on v5–v3 and is absent on v2–v1.</summary>
    private static ScriptedPublishedSource BlameHistory(string? autosaveHandleOnV3 = null)
    {
        var api = new ScriptedPublishedSource();

        foreach (var i in new[] { 5, 4, 3, 2, 1 })
        {
            var handle = i == 3 && autosaveHandleOnV3 is not null ? autosaveHandleOnV3 : "Ann";
            api.Versions.Add(ScriptedPublishedSource.Version($"v{i}", $"Release {i}", handle));
        }

        foreach (var version in new[] { "current", "v5", "v4", "v3" })
        {
            api.PutNode(version, "10:1", ScriptedPublishedSource.ComponentSet("10:1", "Button", ["Disabled#1:2"], ["10:2"]));
        }

        foreach (var version in new[] { "v2", "v1" })
        {
            api.PutNode(version, "10:1", ScriptedPublishedSource.ComponentSet("10:1", "Button"));
        }

        return api;
    }

    [Fact]
    public async Task BlameFindsTheOldestVersionThatStillHasThePropertyWithoutWalkingThemAll()
    {
        var api = BlameHistory();

        var data = Data(await Tools(api).BlameNodeAsync("10:1", "Disabled#1:2", fileUrl: FileUrl));

        Assert.Equal("v3", data["introduced_at"]!["version_id"]!.GetValue<string>());
        Assert.Equal("Ann", data["introduced_at"]!["user_handle"]!.GetValue<string>());
        Assert.Equal("exact", data["attribution_certainty"]!.GetValue<string>());

        Assert.Equal(2, data["summary"]!["probes_made"]!.GetValue<int>());
        Assert.Equal(3, api.NodeCalls);
    }

    [Fact]
    public async Task BlameWorksTheSameWayForAChildNode()
    {
        var api = BlameHistory();

        var data = Data(await Tools(api).BlameNodeAsync("10:1", targetChildNodeId: "10:2", fileUrl: FileUrl));

        Assert.Equal("v3", data["introduced_at"]!["version_id"]!.GetValue<string>());
        Assert.Equal("child_node", data["target"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task BlameSaysWhenItOnlyReachedTheLookbackHorizon()
    {
        var api = BlameHistory();

        var data = Data(await Tools(api).BlameNodeAsync(
            "10:1", "Disabled#1:2", fileUrl: FileUrl, maxVersionsToWalk: 2));

        Assert.Equal("exists_at_lookback_horizon", data["attribution_certainty"]!.GetValue<string>());
        Assert.Contains(
            data["notes"]!.AsArray(),
            note => note!.GetValue<string>().Contains("maxVersionsToWalk", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SystemAutosaveIsFlaggedInsteadOfBeingPassedOffAsAnAuthor()
    {
        var api = BlameHistory(autosaveHandleOnV3: "Figma");

        var data = Data(await Tools(api).BlameNodeAsync("10:1", "Disabled#1:2", fileUrl: FileUrl));

        Assert.Equal("v3", data["introduced_at"]!["version_id"]!.GetValue<string>());
        Assert.Equal("system_attributed", data["attribution_certainty"]!.GetValue<string>());
        Assert.Contains(
            data["notes"]!.AsArray(),
            note => note!.GetValue<string>().Contains("includeAutosaves=false", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TargetAbsentFromEveryOlderVersionIsAttributedToTheStartVersionItself()
    {
        var api = BlameHistory();

        var data = Data(await Tools(api).BlameNodeAsync(
            "10:1", "Disabled#1:2", fileUrl: FileUrl, startVersion: "v3"));

        Assert.Equal("v3", data["introduced_at"]!["version_id"]!.GetValue<string>());
        Assert.Equal(2, data["summary"]!["versions_in_search_range"]!.GetValue<int>());
    }

    [Fact]
    public async Task BlameRefusesWhenTheTargetIsNotThereToBeginWith()
    {
        var api = BlameHistory();

        var response = Parse(await Tools(api).BlameNodeAsync("10:1", "NoSuch#9:9", fileUrl: FileUrl));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("has not appeared", response["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task BlameAlwaysCarriesTheMonotonicityCaveat()
    {
        var api = BlameHistory();

        var data = Data(await Tools(api).BlameNodeAsync("10:1", "Disabled#1:2", fileUrl: FileUrl));

        Assert.Contains(
            data["notes"]!.AsArray(),
            note => note!.GetValue<string>().Contains("monotonic", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Disabled#1:2", "10:2")]
    [InlineData(null, null)]
    public async Task BlameNeedsExactlyOneTarget(string? property, string? childId)
    {
        var response = Parse(await Tools(BlameHistory()).BlameNodeAsync("10:1", property, childId, FileUrl));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Exactly one", response["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownFileUrlFailsWithAReadableMessageNotAStackTrace()
    {
        var response = Parse(await Tools(BlameHistory()).GetFileVersionsAsync("not a link at all"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("link", response["error"]!.GetValue<string>());
    }
}
