using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>FigJam tool parameters: validation, id normalisation and JSON packing decided before the editor extension.</summary>
[Collection(nameof(BridgeHostFixture))]
public class FigJamToolsTests
{
    private readonly BridgeHostFixture _bridge;

    public FigJamToolsTests(BridgeHostFixture bridge) => _bridge = bridge;

    private FigJamTools FigJam() => new(_bridge.Gateway, NullLogger<FigJamTools>.Instance);

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private static JsonNode? SentParameters(FakeEditorExtension plugin, string method) => plugin.Received
        .LastOrDefault(message => message["method"]?.GetValue<string>() == method)?["params"];

    /// <summary>Extracts the params object from generated code: the first line is <c>const params = JSON.parse("…");</c>, and the double parse proves values were not concatenated into code.</summary>
    private static JsonNode ParamsFromGeneratedCode(FakeEditorExtension plugin)
    {
        var code = SentParameters(plugin, EditorCommands.ExecuteCode.Name)!["code"]!.GetValue<string>();
        var firstLine = code[..code.IndexOf('\n')].TrimEnd();

        const string parseCall = "JSON.parse(";
        var start = firstLine.IndexOf(parseCall, StringComparison.Ordinal) + parseCall.Length;
        var literal = firstLine[start..^");".Length];

        return JsonNode.Parse(JsonNode.Parse(literal)!.GetValue<string>())!;
    }

    private async Task<FakeEditorExtension> ConnectAsync()
    {
        var fileKey = NewFileKey();
        var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);

        return plugin;
    }

    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    [Fact]
    public async Task StickyBatchRejectsSomethingThatIsNotAJsonArray()
    {
        await using var plugin = await ConnectAsync();

        var response = Parse(await FigJam().CreateStickiesAsync("""{"text":"single item"}"""));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("array", response["error"]!.GetValue<string>());
        Assert.Null(SentParameters(plugin, EditorCommands.CreateStickies.Name));
    }

    [Fact]
    public async Task StickyBatchIsCappedBeforeItReachesThePlugin()
    {
        await using var plugin = await ConnectAsync();

        var tooMany = new JsonArray(Enumerable
            .Range(0, 201)
            .Select(i => (JsonNode)new JsonObject { ["text"] = $"idea {i}" })
            .ToArray());

        var response = Parse(await FigJam().CreateStickiesAsync(tooMany.ToJsonString()));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("200", response["error"]!.GetValue<string>());
        Assert.Null(SentParameters(plugin, EditorCommands.CreateStickies.Name));
    }

    [Fact]
    public async Task StickyBatchGoesThroughAsOneArray()
    {
        await using var plugin = await ConnectAsync();

        await FigJam().CreateStickiesAsync("""[{"text":"One","color":"BLUE"},{"text":"Two"}]""");

        var sent = SentParameters(plugin, EditorCommands.CreateStickies.Name)!["stickies"]!.AsArray();

        Assert.Equal(2, sent.Count);
        Assert.Equal("One", sent[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConnectorNormalisesDashedNodeIdsFromUrls()
    {
        await using var plugin = await ConnectAsync();

        await FigJam().CreateConnectorAsync("12-34", "56-78");

        var sent = SentParameters(plugin, EditorCommands.CreateConnector.Name)!;

        Assert.Equal("12:34", sent["startNodeId"]!.GetValue<string>());
        Assert.Equal("56:78", sent["endNodeId"]!.GetValue<string>());
    }

    [Fact]
    public async Task AutoArrangeRefusesAnEmptyListOfNodes()
    {
        await using var plugin = await ConnectAsync();

        var response = Parse(await FigJam().AutoArrangeAsync("  ,  , "));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("No node id", response["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task AutoArrangeSendsParametersAsJsonSoIdsCannotInjectCode()
    {
        await using var plugin = await ConnectAsync();

        const string hostile = "1:2\");\nfigma.currentPage.children.forEach(n => n.remove());//";

        await FigJam().AutoArrangeAsync($"1:1,{hostile}", "horizontal", spacing: 12);

        var parameters = ParamsFromGeneratedCode(plugin);
        var ids = parameters["nodeIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray();

        Assert.Equal(["1:1", hostile], ids);
        Assert.Equal("horizontal", parameters["layout"]!.GetValue<string>());
        Assert.Equal(12, parameters["spacing"]!.GetValue<double>());

        var code = SentParameters(plugin, EditorCommands.ExecuteCode.Name)!["code"]!.GetValue<string>();
        Assert.DoesNotContain("\nfigma.currentPage.children.forEach", code);
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 3)]
    public async Task GridColumnsDefaultToTheSquareRootOfTheNodeCount(int nodes, int expectedColumns)
    {
        await using var plugin = await ConnectAsync();

        await FigJam().AutoArrangeAsync(string.Join(',', Enumerable.Range(1, nodes).Select(i => $"1:{i}")));

        Assert.Equal(expectedColumns, ParamsFromGeneratedCode(plugin)["gridCols"]!.GetValue<int>());
    }

    [Fact]
    public async Task AutoArrangeStillPassesNodeIdsSoTheCacheCanBeInvalidated()
    {
        await using var plugin = await ConnectAsync();

        await FigJam().AutoArrangeAsync("1:1,1:2");

        var sent = SentParameters(plugin, EditorCommands.ExecuteCode.Name)!;
        Assert.Equal(["1:1", "1:2"], sent["nodeIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray());
    }
}
