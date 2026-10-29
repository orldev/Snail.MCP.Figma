using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Application.Libraries;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>figma_search_components: where an agent gets the key of a component, from the open file or from a library it names by link.</summary>
[Collection(nameof(BridgeHostFixture))]
public class ComponentSearchTests(BridgeHostFixture bridge)
{
    private const string LibraryLink = "https://www.figma.com/design/LIBKEY1234567890/UI-Kit?node-id=1-2";

    private readonly BridgeHostFixture _bridge = bridge;

    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    private ComponentTools Tools(FakePublishedSource? api = null) =>
        new(_bridge.Gateway, new PublishedLibrary(api ?? new FakePublishedSource(new JsonObject())), NullLogger<ComponentTools>.Instance);

    private static JsonNode Data(string response)
    {
        var envelope = JsonNode.Parse(response)!;
        Assert.True(envelope["ok"]!.GetValue<bool>(), envelope["error"]?.GetValue<string>() ?? "expected success");

        return envelope["data"]!;
    }

    /// <summary>The plugin's answer as the panel relays it: ui.html wraps what code.js posts in <c>{ success, data }</c>.</summary>
    private static JsonObject Relayed(JsonObject data) => new() { ["success"] = true, ["data"] = data };

    private static JsonObject OpenFileAnswer() => new()
    {
        ["componentSets"] = new JsonArray(new JsonObject
        {
            ["key"] = "local-set",
            ["name"] = "Card",
            ["type"] = "COMPONENT_SET",
            ["variants"] = new JsonArray(new JsonObject { ["key"] = "local-v1", ["name"] = "Size=S", ["width"] = 10 }),
            ["defaultVariant"] = new JsonObject { ["key"] = "local-v1" },
        }),
        ["components"] = new JsonArray(new JsonObject { ["key"] = "local-icon", ["name"] = "Icon", ["type"] = "COMPONENT" }),
        ["libraryComponents"] = new JsonArray(
            new JsonObject
            {
                ["key"] = "lib-button-l",
                ["name"] = "Size=Large",
                ["set"] = new JsonObject { ["key"] = "lib-button", ["name"] = "Button" },
                ["instances"] = 12,
            },
            new JsonObject { ["key"] = "lib-avatar", ["name"] = "Avatar", ["instances"] = 3 }),
        ["libraryInstancesScanned"] = 15,
        ["libraryInstancesTotal"] = 15,
    };

    private static FakePublishedSource Library() => new(new JsonObject())
    {
        ComponentSetsResponse = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["component_sets"] = new JsonArray(new JsonObject
                {
                    ["key"] = "set-button",
                    ["node_id"] = "1:1",
                    ["name"] = "Button",
                    ["description"] = "",
                    ["containing_frame"] = new JsonObject { ["pageName"] = "Controls" },
                }),
            },
        },
        ComponentsResponse = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["components"] = new JsonArray(
                    new JsonObject
                    {
                        ["key"] = "v-button-l",
                        ["node_id"] = "1:2",
                        ["name"] = "Size=Large",
                        ["description"] = "",
                        ["containing_frame"] = new JsonObject
                        {
                            ["pageName"] = "Controls",
                            ["containingComponentSet"] = new JsonObject { ["name"] = "Button", ["nodeId"] = "1:1" },
                        },
                    },
                    new JsonObject
                    {
                        ["key"] = "c-avatar",
                        ["node_id"] = "3:1",
                        ["name"] = "Avatar",
                        ["description"] = "",
                        ["containing_frame"] = new JsonObject { ["pageName"] = "Media" },
                    }),
            },
        },
    };

    /// <summary>The search answered nothing through the plugin: it looked for the lists at the top of the answer, where the panel's <c>{ success, data }</c> has none, and it read only standalone components, never the sets.</summary>
    [Fact]
    public async Task SearchComponents_OpenFile_ListsItsSetsAndTheLibraryComponentsItUses()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);
        plugin.Handlers[EditorCommands.GetLocalComponents.Name] = _ => Relayed(OpenFileAnswer());

        var data = Data(await Tools().SearchComponentsAsync());

        Assert.Equal(["local-set", "local-icon"], data["components"]!.AsArray().Select(component => component!["key"]!.GetValue<string>()));
        Assert.Equal(["lib-button-l", "lib-avatar"], data["library"]!.AsArray().Select(component => component!["key"]!.GetValue<string>()));
        Assert.False(data["components"]![0]!.AsObject().ContainsKey("defaultVariant"));
        Assert.False(data["components"]![0]!["variants"]![0]!.AsObject().ContainsKey("width"));
        Assert.Contains(plugin.Received, message =>
            message["method"]?.GetValue<string>() == EditorCommands.GetLocalComponents.Name
            && message["params"]?["includeLibrary"]?.GetValue<bool>() == true);
    }

    [Fact]
    public async Task SearchComponents_OpenFileQuery_FindsALibraryVariantByItsSet()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);
        plugin.Handlers[EditorCommands.GetLocalComponents.Name] = _ => Relayed(OpenFileAnswer());

        var data = Data(await Tools().SearchComponentsAsync(query: "button"));

        Assert.Empty(data["components"]!.AsArray());
        Assert.Equal("lib-button-l", data["library"]![0]!["key"]!.GetValue<string>());
    }

    [Fact]
    public async Task SearchComponents_PluginCannotList_AnswersWithItsReason()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);
        plugin.Handlers[EditorCommands.GetLocalComponents.Name] = _ => new JsonObject { ["success"] = false, ["error"] = "loadAllPagesAsync failed" };

        var envelope = JsonNode.Parse(await Tools().SearchComponentsAsync())!;

        Assert.False(envelope["ok"]!.GetValue<bool>());
        Assert.Contains("loadAllPagesAsync failed", envelope["error"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void OpenFileComponents_InstancesBeyondTheCeiling_SaysHowManyWereLookedAt()
    {
        var answer = OpenFileAnswer();
        answer["libraryInstancesScanned"] = 5000;
        answer["libraryInstancesTotal"] = 7200;

        var data = OpenFileComponents.Match(Relayed(answer), null, 50);

        Assert.Equal(5000, data["libraryInstancesScanned"]!.GetValue<int>());
        Assert.Equal(7200, data["libraryInstancesTotal"]!.GetValue<int>());
    }

    /// <summary>A library is read by its link without the plugin: no file has to be open in Figma.</summary>
    [Fact]
    public async Task SearchComponents_LibraryLink_ListsWhatTheLibraryPublishesWithoutThePlugin()
    {
        var data = Data(await Tools(Library()).SearchComponentsAsync(libraryFileUrl: LibraryLink));

        Assert.Equal("library", data["source"]!.GetValue<string>());
        Assert.Equal("LIBKEY1234567890", data["fileKey"]!.GetValue<string>());
        Assert.Equal(["set-button", "c-avatar"], data["items"]!.AsArray().Select(item => item!["key"]!.GetValue<string>()));
        Assert.Equal(1, data["items"]![0]!["variants"]!.GetValue<int>());
    }

    [Fact]
    public async Task SearchComponents_LibraryLinkWithVariants_GivesEachVariantItsKey()
    {
        var data = Data(await Tools(Library()).SearchComponentsAsync(query: "button", libraryFileUrl: LibraryLink, includeVariants: true));

        Assert.Equal(["set-button", "v-button-l"], data["items"]!.AsArray().Select(item => item!["key"]!.GetValue<string>()));
        Assert.Equal("Button", data["items"]![1]!["set"]!.GetValue<string>());
    }

    [Fact]
    public async Task SearchComponents_LibraryPageCut_HintsTheNextOffset()
    {
        var envelope = JsonNode.Parse(await Tools(Library()).SearchComponentsAsync(libraryFileUrl: LibraryLink, limit: 1))!;

        Assert.Equal(1, envelope["data"]!["nextOffset"]!.GetValue<int>());
        Assert.Contains("offset=1", envelope["hint"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchComponents_LibraryPublishingNothing_SaysWhyItMayBeEmpty()
    {
        var envelope = JsonNode.Parse(await Tools().SearchComponentsAsync(libraryFileUrl: LibraryLink))!;

        Assert.True(envelope["ok"]!.GetValue<bool>());
        Assert.Equal(Messages.NothingPublishedHint, envelope["hint"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("https://www.figma.com/design/ABCDEFGHIJ12/Kit/branch/BRANCHKEY99/Kit", "ABCDEFGHIJ12")]
    [InlineData("ABCDEFGHIJ12", "ABCDEFGHIJ12")]
    [InlineData("  https://www.figma.com/file/ABCDEFGHIJ12/Kit  ", "ABCDEFGHIJ12")]
    public void FileKeyOf_LinkOrKey_NamesTheMainFile(string link, string expected)
    {
        Assert.Equal(expected, PublishedLibrary.FileKeyOf(link));
    }

    [Fact]
    public async Task SearchComponents_LinkWithoutAFile_AnswersWithHowToPassIt()
    {
        var envelope = JsonNode.Parse(await Tools().SearchComponentsAsync(libraryFileUrl: "the UI kit"))!;

        Assert.False(envelope["ok"]!.GetValue<bool>());
        Assert.Equal(Messages.LibraryFileLinkHint, envelope["hint"]!.GetValue<string>());
    }
}
