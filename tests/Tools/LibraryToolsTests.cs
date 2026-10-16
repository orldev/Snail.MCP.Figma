using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Library tool lookup order: component set first, single component as the fallback.</summary>
[Collection(nameof(BridgeHostFixture))]
public class LibraryToolsTests(BridgeHostFixture bridge)
{
    private readonly BridgeHostFixture _bridge = bridge;

    private LibraryTools Library(FakePublishedSource api) => new(
        _bridge.Gateway, api, api, NullLogger<LibraryTools>.Instance);

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private static JsonNode Data(string response)
    {
        var envelope = Parse(response);
        Assert.True(envelope["ok"]!.GetValue<bool>(), envelope["error"]?.GetValue<string>() ?? "expected success");

        return envelope["data"]!;
    }

    private static FakePublishedSource LibraryApi(bool asComponentSet)
    {
        var api = new FakePublishedSource(new JsonObject());
        var meta = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["file_key"] = "LIBKEY",
                ["node_id"] = "10:1",
                ["name"] = "Button",
                ["description"] = "Button",
            },
        };

        api.Endpoints[asComponentSet ? "/v1/component_sets/abc" : "/v1/components/abc"] = meta;

        if (asComponentSet)
        {
            api.NotFound.Add("/v1/components/");
        }
        else
        {
            api.NotFound.Add("/v1/component_sets/");
        }

        api.NodeDocuments["10:1"] = new JsonObject
        {
            ["id"] = "10:1",
            ["name"] = "Button",
            ["type"] = asComponentSet ? "COMPONENT_SET" : "COMPONENT",
            ["componentPropertyDefinitions"] = new JsonObject
            {
                ["Size#1:1"] = new JsonObject { ["type"] = "VARIANT", ["defaultValue"] = "M" },
            },
            ["children"] = new JsonArray(
                new JsonObject { ["id"] = "10:2", ["name"] = "Size=S", ["type"] = "COMPONENT" },
                new JsonObject { ["id"] = "10:3", ["name"] = "Size=M", ["type"] = "COMPONENT" }),
        };

        api.ComponentsResponse = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["components"] = new JsonArray(
                    new JsonObject { ["node_id"] = "10:2", ["key"] = "key-s" },
                    new JsonObject { ["node_id"] = "10:3", ["key"] = "key-m" }),
            },
        };

        return api;
    }

    [Fact]
    public async Task LibraryTools_KeyOfComponentSet_AsksSetFirstAndCarriesVariantKeys()
    {
        var api = LibraryApi(asComponentSet: true);

        var data = Data(await Library(api).GetLibraryComponentByKeyAsync("abc"));

        Assert.Equal("/v1/component_sets/abc", api.RequestedEndpoints[0]);
        Assert.Equal("COMPONENT_SET", data["resolvedAs"]!.GetValue<string>());
        Assert.Equal("key-s", data["variants"]![0]!["key"]!.GetValue<string>());

        Assert.Contains("VARIANT", data["usage"]!.GetValue<string>());
    }

    [Fact]
    public async Task LibraryTools_KeyOfSingleComponent_FindsItThroughFallback()
    {
        var api = LibraryApi(asComponentSet: false);

        var data = Data(await Library(api).GetLibraryComponentByKeyAsync("abc"));

        Assert.Equal(["/v1/component_sets/abc", "/v1/components/abc"], [.. api.RequestedEndpoints]);
        Assert.Equal("COMPONENT", data["resolvedAs"]!.GetValue<string>());
        Assert.Equal("LIBKEY", data["fileKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task LibraryTools_UnknownComponentKey_ErrorNamesWhereKeysComeFrom()
    {
        var api = new FakePublishedSource(new JsonObject());
        api.NotFound.Add("/v1/component");

        var response = Parse(await Library(api).GetLibraryComponentByKeyAsync("no-such-key"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("figma_search_components", response["hint"]!.GetValue<string>());
    }

    [Fact]
    public async Task LibraryTools_MissingStyleKeyAnswers403_ReadsSameAsForeignStyle()
    {
        var api = new FakePublishedSource(new JsonObject());
        api.Forbidden.Add("/v1/styles/");

        var response = Parse(await Library(api).GetLibraryStyleByKeyAsync("no-such-key"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Forbidden", response["error"]!.GetValue<string>());
        Assert.Contains("figma_get_styles", response["hint"]!.GetValue<string>());
    }
}
