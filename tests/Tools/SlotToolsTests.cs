using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Slot tool parameters: addressing and property validation decided before the editor extension.</summary>
[Collection(nameof(BridgeHostFixture))]
public class SlotToolsTests(BridgeHostFixture bridge)
{
    private readonly BridgeHostFixture _bridge = bridge;

    private SlotTools Slots() => new(_bridge.Gateway, NullLogger<SlotTools>.Instance);

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private static JsonNode? SentParameters(FakeEditorExtension plugin, string method) => plugin.Received
        .LastOrDefault(message => message["method"]?.GetValue<string>() == method)?["params"];

    private async Task<FakeEditorExtension> ConnectAsync()
    {
        var fileKey = NewFileKey();
        var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.TryClaimAsync(fileKey);

        return plugin;
    }

    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    [Fact]
    public async Task AppendToSlotNeedsAnAddressedSlot()
    {
        await using var plugin = await ConnectAsync();

        var response = Parse(await Slots().AppendToSlotAsync(instanceId: "1:1", nodeType: "FRAME"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("not addressed", response["error"]!.GetValue<string>());
        Assert.Contains("figma_get_slots", response["hint"]!.GetValue<string>());
    }

    [Fact]
    public async Task AppendToSlotNeedsToKnowWhatToPutInside()
    {
        await using var plugin = await ConnectAsync();

        var response = Parse(await Slots().AppendToSlotAsync(slotId: "1:5"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("what to put", response["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task AppendToSlotReportsMalformedPropertiesInsteadOfSendingThem()
    {
        await using var plugin = await ConnectAsync();

        var response = Parse(await Slots().AppendToSlotAsync(
            slotId: "1:5", nodeType: "TEXT", properties: "{not json"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("properties", response["error"]!.GetValue<string>());
        Assert.Null(SentParameters(plugin, EditorCommands.AppendToSlot.Name));
    }

    [Fact]
    public async Task AppendToSlotByInstanceAndNameNormalisesEveryIdentifier()
    {
        await using var plugin = await ConnectAsync();

        await Slots().AppendToSlotAsync(
            instanceId: "10-1",
            slotName: "Content",
            sourceNodeId: "20-3",
            properties: """{"name":"Icon"}""");

        var sent = SentParameters(plugin, EditorCommands.AppendToSlot.Name)!;

        Assert.Equal("10:1", sent["instanceId"]!.GetValue<string>());
        Assert.Equal("20:3", sent["sourceNodeId"]!.GetValue<string>());
        Assert.Equal("Content", sent["slotName"]!.GetValue<string>());
        Assert.Equal("Icon", sent["properties"]!["name"]!.GetValue<string>());
        Assert.True(sent["clone"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ResetSlotNeedsAnAddressedSlotToo()
    {
        await using var plugin = await ConnectAsync();

        var response = Parse(await Slots().ResetSlotAsync(slotName: "Content"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("not addressed", response["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task SlotPropertyRejectsMalformedPreferredValues()
    {
        await using var plugin = await ConnectAsync();

        var response = Parse(await Slots().AddSlotPropertyAsync("1:1", "Content", "1:2", preferredValues: "[{"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("preferredValues", response["error"]!.GetValue<string>());
    }
}
