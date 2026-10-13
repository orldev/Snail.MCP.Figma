using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Block-B REST additions: token probe, cheap file meta, style by key, team-wide search.</summary>
public class RestAdditionTests
{
    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private static JsonNode Data(string response)
    {
        var envelope = Parse(response);
        Assert.True(envelope["ok"]!.GetValue<bool>(), envelope["error"]?.GetValue<string>() ?? "expected success");

        return envelope["data"]!;
    }

    private static LibraryTools Library(BridgelessEditor plugin, FakePublishedSource api) =>
        new(plugin, api, api, NullLogger<LibraryTools>.Instance);

    [Fact]
    public async Task StyleByKeyReturnsTheMetaFields()
    {
        var api = new FakePublishedSource(new JsonObject());
        api.Endpoints["/v1/styles/st1"] = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["key"] = "st1",
                ["name"] = "Brand/Primary",
                ["style_type"] = "FILL",
                ["description"] = "Primary color",
                ["file_key"] = "LIB",
                ["node_id"] = "5:1",
                ["updated_at"] = "2026-08-01T00:00:00Z",
                ["user"] = new JsonObject { ["handle"] = "Ann" },
            },
        };

        var data = Data(await Library(new BridgelessEditor(), api).GetLibraryStyleByKeyAsync("st1"));

        Assert.Equal("Brand/Primary", data["name"]!.GetValue<string>());
        Assert.Equal("FILL", data["styleType"]!.GetValue<string>());
        Assert.Equal("LIB", data["fileKey"]!.GetValue<string>());
        Assert.Equal("Ann", data["creator"]!.GetValue<string>());
    }

    [Fact]
    public async Task StyleByKeyExplainsTheNotFound()
    {
        var api = new FakePublishedSource(new JsonObject());
        api.NotFound.Add("/v1/styles/");

        var envelope = Parse(await Library(new BridgelessEditor(), api).GetLibraryStyleByKeyAsync("missing"));

        Assert.False(envelope["ok"]!.GetValue<bool>());
        Assert.Contains("not published", envelope["hint"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("1234567890", "1234567890")]
    [InlineData(" 42 ", "42")]
    [InlineData("https://www.figma.com/files/team/987654321/Keynex", "987654321")]
    public void TeamIdIsReadFromDigitsOrUrl(string input, string expected)
    {
        Assert.True(LibraryTools.TryParseTeamId(input, out var teamId));
        Assert.Equal(expected, teamId);
    }

    [Fact]
    public void ATeamlessUrlIsRejected()
    {
        Assert.False(LibraryTools.TryParseTeamId("https://www.figma.com/design/abc/Name", out _));
    }

    [Fact]
    public async Task TeamSearchFiltersBothKindsAndKeepsTheCursor()
    {
        var api = new FakePublishedSource(new JsonObject());
        api.Endpoints["/v1/teams/42/components?page_size=100"] = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["components"] = new JsonArray(
                    new JsonObject { ["key"] = "c1", ["name"] = "Button", ["file_key"] = "LIB", ["node_id"] = "1:1" },
                    new JsonObject { ["key"] = "c2", ["name"] = "Input", ["file_key"] = "LIB", ["node_id"] = "1:2" }),
                ["cursor"] = new JsonObject { ["after"] = "42:100" },
            },
        };
        api.Endpoints["/v1/teams/42/component_sets?page_size=100"] = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["component_sets"] = new JsonArray(
                    new JsonObject { ["key"] = "s1", ["name"] = "Button Set", ["file_key"] = "LIB", ["node_id"] = "2:1" }),
            },
        };

        var data = Data(await Library(new BridgelessEditor(), api).SearchTeamComponentsAsync("42", "button"));

        Assert.Equal(2, data["matched"]!.GetValue<int>());
        var kinds = data["items"]!.AsArray().Select(i => i!["kind"]!.GetValue<string>()).ToList();
        Assert.Contains("COMPONENT", kinds);
        Assert.Contains("COMPONENT_SET", kinds);
        Assert.Equal("42:100", data["nextCursor"]!.GetValue<string>());
    }

    private static DiagnosticsTools Diagnostics(ServerConfig config, FakePublishedSource api) => new(
        config,
        new StubCacheHealth(),
        new StubLink(),
        new StubDocuments(),
        new StubQuota(),
        new StubInstaller(),
        api);

    [Fact]
    public async Task TokenProbeReportsTheIdentity()
    {
        var api = new FakePublishedSource(new JsonObject());
        api.Endpoints["/v1/me"] = new JsonObject { ["handle"] = "Ann", ["email"] = "ann@example.com" };
        var config = new ServerConfig { AccessToken = "figd_test" };

        var report = JsonNode.Parse(await Diagnostics(config, api).DiagnoseAsync(probeToken: true))!;
        var token = report["config"]!["figmaAccessToken"]!;

        Assert.Equal("valid", token["status"]!.GetValue<string>());
        Assert.Equal("Ann", token["handle"]!.GetValue<string>());
    }

    [Fact]
    public async Task TokenProbeSurfacesTheRejection()
    {
        var api = new FakePublishedSource(new JsonObject());
        api.NotFound.Add("/v1/me");
        var config = new ServerConfig { AccessToken = "figd_expired" };

        var report = JsonNode.Parse(await Diagnostics(config, api).DiagnoseAsync(probeToken: true))!;
        var token = report["config"]!["figmaAccessToken"]!;

        Assert.Equal("REJECTED by Figma", token["status"]!.GetValue<string>());
        Assert.Contains("figma.com", token["hint"]!.GetValue<string>());
    }

    [Fact]
    public async Task WithoutTheProbeTheTokenIsOnlyDescribed()
    {
        var api = new FakePublishedSource(new JsonObject());
        var config = new ServerConfig { AccessToken = "figd_test" };

        var report = JsonNode.Parse(await Diagnostics(config, api).DiagnoseAsync())!;

        Assert.Contains("probeToken=true", report["config"]!["figmaAccessToken"]!.GetValue<string>());
        Assert.Empty(api.RequestedEndpoints);
    }

    /// <summary>REST-only tools must not touch the live editor; every call here is a test failure.</summary>
    private sealed class BridgelessEditor : ILiveEditor
    {
        public bool IsConnected => false;

        public IOpenDocuments Documents => new StubDocuments();

        public Task<JsonNode?> ReadAsync(Snail.MCP.Figma.Application.Editor.EditorCommand command, JsonObject? parameters = null, string? fileKey = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("REST tool called the plugin");

        public Task<JsonNode?> ApplyAsync(Snail.MCP.Figma.Application.Editor.EditorCommand command, JsonObject parameters, string? fileKey = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("REST tool called the plugin");

        public Task<JsonNode?> ReadTreeAsync(string fileKey, string? nodeId, int depth, int maxNodes, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("REST tool called the plugin");

        public Task<JsonNode?> ReadStylesAsync(string fileKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("REST tool called the plugin");

        public Task InvalidateWholeFileAsync(string fileKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubCacheHealth : ISnapshotStoreHealth
    {
        public bool IsStarted => false;

        public string? LastError => null;

        public SnapshotStoreProbe Probe() => SnapshotStoreProbe.Failed("disabled");
    }

    private sealed class StubLink : ILiveEditorLink
    {
        public bool IsListening => false;

        public string? Address => null;
    }

    private sealed class StubDocuments : IOpenDocuments
    {
        public IReadOnlyCollection<IOpenDocument> Sessions => [];

        public bool HasConnection => false;

        public string? ActiveFileKey => null;

        public IOpenDocument? LastTouched => null;

        public Task<bool> TryClaimAsync(string fileKey, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public IOpenDocument? Resolve(string? fileKey = null) => null;

        public Remarks RemarksIn(IOpenDocument document) => new(20);

        public Task PublishRemarksWaitingAsync(IOpenDocument document, CancellationToken cancellationToken) => Task.CompletedTask;

        public long ConsoleMark(IOpenDocument document) => 0;

        public void ClearConsole(IOpenDocument document)
        {
        }
    }

    private sealed class StubQuota : IQuotaStatus
    {
        public IReadOnlyDictionary<string, DateTimeOffset> ActiveBlocks { get; } = new Dictionary<string, DateTimeOffset>();
    }

    private sealed class StubInstaller : IEditorExtensionInstaller
    {
        public string? BundledDirectory => null;

        public string TargetDirectory => "/tmp/none";

        public bool IsInstalled => false;
    }
}
