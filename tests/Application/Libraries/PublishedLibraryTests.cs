using Snail.MCP.Figma.Application.Libraries;

namespace Snail.MCP.Figma.Tests.Application.Libraries;

/// <summary>The catalog of a library file, read from what Figma publishes: what an agent picks a component from and places it by.</summary>
public class PublishedLibraryTests
{
    private const string LibraryKey = "LIBKEY";

    private static JsonObject Frame(string page, string? setName = null, string? setNodeId = null)
    {
        var frame = new JsonObject { ["pageName"] = page, ["name"] = "Frame" };

        if (setName is not null)
        {
            frame["containingComponentSet"] = new JsonObject { ["name"] = setName, ["nodeId"] = setNodeId };
        }

        return frame;
    }

    private static JsonObject Published(string key, string name, string nodeId, JsonObject frame, string description = "") => new()
    {
        ["key"] = key,
        ["file_key"] = LibraryKey,
        ["node_id"] = nodeId,
        ["name"] = name,
        ["description"] = description,
        ["containing_frame"] = frame,
    };

    private static JsonObject Sets() => new()
    {
        ["meta"] = new JsonObject
        {
            ["component_sets"] = new JsonArray(
                Published("set-button", "Button", "1:1", Frame("Controls"), "Primary actions"),
                Published("set-input", "Input", "2:1", Frame("Forms"))),
        },
    };

    private static JsonObject Components() => new()
    {
        ["meta"] = new JsonObject
        {
            ["components"] = new JsonArray(
                Published("v-button-s", "Size=Small", "1:2", Frame("Controls", "Button", "1:1")),
                Published("v-button-l", "Size=Large", "1:3", Frame("Controls", "Button", "1:1")),
                Published("v-input", "State=Default", "2:2", Frame("Forms", "Input", "2:1")),
                Published("c-avatar", "Avatar", "3:1", Frame("Media"))),
        },
    };

    private static LibraryQuery Query(string? text = null, bool variants = false, int offset = 0, int limit = PublishedLibrary.DefaultLimit) =>
        new(LibraryKey, text, variants, offset, limit);

    [Fact]
    public void Catalog_ByDefault_ListsSetsWithTheirVariantCountThenStandaloneComponents()
    {
        var page = PublishedLibrary.Catalog(Query(), Sets(), Components());

        Assert.Equal(["set-button", "set-input", "c-avatar"], page.Entries.Select(entry => entry.Key));
        Assert.Equal([2, 1, null], page.Entries.Select(entry => entry.Variants));
        Assert.Equal(LibraryEntry.ComponentSet, page.Entries[0].Kind);
        Assert.Equal(LibraryEntry.Component, page.Entries[2].Kind);
    }

    [Fact]
    public void Catalog_VariantsAsked_ListsEachUnderItsSet()
    {
        var page = PublishedLibrary.Catalog(Query(variants: true), Sets(), Components());

        Assert.Equal(["set-button", "v-button-s", "v-button-l", "set-input", "v-input", "c-avatar"], page.Entries.Select(entry => entry.Key));
        Assert.Equal("Button", page.Entries[1].SetName);
        Assert.Equal("Button", page.Entries[1].Describe()["set"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("button", new[] { "set-button" })]
    [InlineData("primary", new[] { "set-button" })]
    [InlineData("forms", new[] { "set-input" })]
    [InlineData("AVATAR", new[] { "c-avatar" })]
    public void Catalog_Query_MatchesNameDescriptionOrPageIgnoringCase(string text, string[] expected)
    {
        var page = PublishedLibrary.Catalog(Query(text), Sets(), Components());

        Assert.Equal(expected, page.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void Catalog_QueryWithVariants_FindsVariantsBySetName()
    {
        var page = PublishedLibrary.Catalog(Query("button", variants: true), Sets(), Components());

        Assert.Equal(["set-button", "v-button-s", "v-button-l"], page.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void Catalog_PageSmallerThanTheMatches_PointsAtTheNextPage()
    {
        var first = PublishedLibrary.Catalog(Query(limit: 2), Sets(), Components());
        var last = PublishedLibrary.Catalog(Query(offset: first.NextOffset!.Value, limit: 2), Sets(), Components());

        Assert.Equal(3, first.Matched);
        Assert.Equal(2, first.NextOffset);
        Assert.Equal(["c-avatar"], last.Entries.Select(entry => entry.Key));
        Assert.Null(last.NextOffset);
    }

    [Fact]
    public void Catalog_NothingPublished_IsAnEmptyLibrary()
    {
        var page = PublishedLibrary.Catalog(Query(), new JsonObject(), new JsonObject { ["meta"] = new JsonObject { ["components"] = new JsonArray() } });

        Assert.True(page.IsEmptyLibrary);
        Assert.Empty(page.Entries);
    }

    [Fact]
    public void Describe_EntryWithoutDescription_LeavesTheFieldOut()
    {
        var page = PublishedLibrary.Catalog(Query("input"), Sets(), Components());

        var described = page.Entries[0].Describe();

        Assert.False(described.ContainsKey("description"));
        Assert.Equal("Forms", described["page"]!.GetValue<string>());
        Assert.Equal(1, described["variants"]!.GetValue<int>());
    }

    [Fact]
    public async Task ReadAsync_FromTheSource_ReadsBothListsOfTheFile()
    {
        var api = new FakePublishedSource(new JsonObject()) { ComponentSetsResponse = Sets(), ComponentsResponse = Components() };

        var page = await new PublishedLibrary(api).ReadAsync(Query(), CancellationToken.None);

        Assert.Equal(2, page.PublishedSets);
        Assert.Equal(4, page.PublishedComponents);
        Assert.Equal(3, page.Matched);
    }
}
