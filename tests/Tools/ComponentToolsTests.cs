using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Tool behaviour over a live bridge: parameter parsing, partial failures, invalidation.</summary>
[Collection(nameof(BridgeHostFixture))]
public class ComponentToolsTests(BridgeHostFixture bridge)
{
    private readonly BridgeHostFixture _bridge = bridge;

    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    private VariableTools Variables() => new(_bridge.Gateway, NullLogger<VariableTools>.Instance);

    private NodeTools Nodes() => new(_bridge.Gateway, new FakeImageFiles(), NullLogger<NodeTools>.Instance);

    private WriteTools Writes() => new(_bridge.Gateway, NullLogger<WriteTools>.Instance);

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    [Fact]
    public async Task BatchCreateReportsEachFailureInsteadOfAbortingTheWholePack()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);

        var attempt = 0;
        plugin.Handlers[EditorCommands.CreateVariable.Name] = parameters => new JsonObject
        {
            ["variableId"] = $"VariableID:1:{++attempt}",
            ["name"] = parameters?["name"]?.DeepClone(),
        };

        var response = Parse(await Variables().BatchCreateVariablesAsync(
            "VariableCollectionId:1:1",
            """[{"name":"color/bg","resolvedType":"COLOR"},{"resolvedType":"COLOR"},{"name":"color/fg","resolvedType":"COLOR"}]"""));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(2, (response["data"]!["created"] as JsonArray)!.Count);

        var failed = (response["data"]!["failed"] as JsonArray)!;
        Assert.Single(failed);
        Assert.Contains("name", failed[0]!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task MalformedJsonParameterComesBackAsAReadableError()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);

        var response = Parse(await Nodes().SetStrokesAsync("1:2", "{not json"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("strokes", response["error"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(response["hint"]?.GetValue<string>()));
    }

    [Fact]
    public async Task VariantPropertyWithoutDefaultIsRejectedBeforeReachingThePlugin()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);

        var tools = new ComponentTools(_bridge.Gateway, NullLogger<ComponentTools>.Instance);
        var response = Parse(await tools.AddComponentPropertyAsync("1:2", "Size", "VARIANT"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.DoesNotContain(plugin.Received, m =>
            m["method"]?.GetValue<string>() == EditorCommands.AddComponentProperty.Name);
    }

    [Fact]
    public async Task DisconnectedBridgeProducesAnActionableHintNotACrash()
    {
        var response = Parse(await Writes().MoveNodeAsync("1:2", 10, 20));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Snail Bridge", response["error"]!.GetValue<string>());
        Assert.Contains("Plugins → Development", response["hint"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null, """{"Size":["S","M","L"],"State":["Default","Hover"]}""", 6)]
    [InlineData("""["1:1","1:2","1:3"]""", null, 3)]
    [InlineData(null, null, 1)]
    public void VariantCountDrivesTheComponentSetTimeout(string? ids, string? properties, int expected)
    {
        var count = ComponentTools.CountVariants(
            ids is null ? null : JsonNode.Parse(ids),
            properties is null ? null : JsonNode.Parse(properties));

        Assert.Equal(expected, count);
    }

    [Fact]
    public void AffectedNodeIdsFallBackToTheRequestWhenThePluginStaysSilent()
    {
        var affected = LiveEditorGateway.ExtractAffectedNodeIds(
            new JsonObject { ["success"] = true },
            new JsonObject { ["nodeId"] = "1-234" });

        Assert.Equal(["1:234"], affected);
    }

    [Fact]
    public void AffectedNodeIdsPreferWhatThePluginActuallyTouched()
    {
        var affected = LiveEditorGateway.ExtractAffectedNodeIds(
            new JsonObject { ["nodeIds"] = new JsonArray("2:1", "2:2") },
            new JsonObject { ["nodeId"] = "1:234" });

        Assert.Equal(["2:1", "2:2"], [.. affected.OrderBy(id => id)]);
    }

    [Fact]
    public void AncestorsAreCollectedSeparatelyForAutoLayoutReflow()
    {
        var ancestors = LiveEditorGateway.ExtractAncestorIds(new JsonObject
        {
            ["parentId"] = "1:1",
            ["ancestorIds"] = new JsonArray("0:1"),
        });

        Assert.Equal(["0:1", "1:1"], [.. ancestors.OrderBy(id => id)]);
    }
}
