using Snail.MCP.Figma.Application.Diff;

namespace Snail.MCP.Figma.Tests.Application.Diff;

/// <summary>Pure part of version comparison: the diff engine, the changelog and the snapshot cache — no network.</summary>
public class VersionDiffTests
{
    private static JsonObject Page(string id, string name) => new() { ["id"] = id, ["name"] = name };

    private static JsonObject Document(params JsonObject[] pages) =>
        new() { ["children"] = new JsonArray(pages.Cast<JsonNode>().ToArray()) };

    private static JsonObject Node(
        string id,
        string name,
        string type = "COMPONENT_SET",
        string? description = null,
        JsonObject? properties = null,
        JsonArray? children = null,
        JsonObject? boundVariables = null)
    {
        var node = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["type"] = type,
        };

        if (description is not null) node["description"] = description;
        if (properties is not null) node["componentPropertyDefinitions"] = properties;
        if (children is not null) node["children"] = children;
        if (boundVariables is not null) node["boundVariables"] = boundVariables;

        return node;
    }

    private static JsonObject Alias(string id) => new() { ["type"] = "VARIABLE_ALIAS", ["id"] = id };

    private static JsonObject Property(string type, JsonNode? defaultValue) =>
        new() { ["type"] = type, ["defaultValue"] = defaultValue };

    private static int Count(JsonNode? node) => (node as JsonArray)?.Count ?? 0;

    [Fact]
    public void PageStructureSeparatesRenamesFromAddsAndRemoves()
    {
        var diff = DiffEngine.DiffPageStructure(
            Document(Page("0:1", "Cover"), Page("0:2", "Components"), Page("0:3", "Draft")),
            Document(Page("0:1", "Cover"), Page("0:2", "Components v2"), Page("0:4", "Icons")));

        Assert.Equal(1, diff["summary"]!["added"]!.GetValue<int>());
        Assert.Equal(1, diff["summary"]!["removed"]!.GetValue<int>());
        Assert.Equal(1, diff["summary"]!["renamed"]!.GetValue<int>());

        Assert.Equal("0:2", diff["pages_renamed"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("Components", diff["pages_renamed"]![0]!["old_name"]!.GetValue<string>());
        Assert.Equal("Components v2", diff["pages_renamed"]![0]!["new_name"]!.GetValue<string>());
        Assert.Equal("0:4", diff["pages_added"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("0:3", diff["pages_removed"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void EmptyDocumentsCompareToNoChangesInsteadOfThrowing()
    {
        var diff = DiffEngine.DiffPageStructure(null, new JsonObject());

        Assert.Equal(0, diff["summary"]!["added"]!.GetValue<int>());
        Assert.Equal(0, diff["summary"]!["removed"]!.GetValue<int>());
    }

    [Fact]
    public void RenamedComponentPropertyReadsAsRemovalPlusAddition()
    {
        var diff = DiffEngine.DiffComponentPropertyDefinitions(
            new JsonObject { ["Disabled#1:2"] = Property("BOOLEAN", false) },
            new JsonObject { ["Inactive#1:2"] = Property("BOOLEAN", false) });

        Assert.Equal(1, diff["summary"]!["added"]!.GetValue<int>());
        Assert.Equal(1, diff["summary"]!["removed"]!.GetValue<int>());
        Assert.Equal(0, diff["summary"]!["type_changed"]!.GetValue<int>());
    }

    [Fact]
    public void TypeAndDefaultChangesAreReportedSeparately()
    {
        var diff = DiffEngine.DiffComponentPropertyDefinitions(
            new JsonObject
            {
                ["Size#1:1"] = Property("VARIANT", "S"),
                ["Label#1:3"] = Property("TEXT", "Button"),
            },
            new JsonObject
            {
                ["Size#1:1"] = Property("TEXT", "S"),
                ["Label#1:3"] = Property("TEXT", "Submit"),
            });

        Assert.Equal(1, diff["summary"]!["type_changed"]!.GetValue<int>());
        Assert.Equal(1, diff["summary"]!["default_changed"]!.GetValue<int>());
        Assert.Equal("Size#1:1", diff["type_changed"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Label#1:3", diff["default_changed"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void BindingChangesDistinguishAddedRemovedAndRebound()
    {
        var before = Node("1:1", "Button", children: new JsonArray(
            Node("1:2", "Label", "TEXT", boundVariables: new JsonObject
            {
                ["characters"] = Alias("VariableID:9:1"),
                ["opacity"] = Alias("VariableID:9:5"),
            })));

        var after = Node("1:1", "Button", children: new JsonArray(
            Node("1:2", "Label", "TEXT", boundVariables: new JsonObject
            {
                ["characters"] = Alias("VariableID:9:2"),
                ["fontSize"] = Alias("VariableID:9:7"),
            })));

        var changes = DiffEngine.CollectBindingChanges(before, after);
        var byProperty = changes.ToDictionary(
            change => change!["property"]!.GetValue<string>(),
            change => change!["change_kind"]!.GetValue<string>());

        Assert.Equal("rebound", byProperty["characters"]);
        Assert.Equal("removed", byProperty["opacity"]);
        Assert.Equal("added", byProperty["fontSize"]);
        Assert.All(changes, change => Assert.Equal("1:2", change!["node_id"]!.GetValue<string>()));
    }

    [Fact]
    public void FillBindingArraysAreComparedByTheirFirstAlias()
    {
        var before = Node("1:1", "Card", boundVariables: new JsonObject
        {
            ["fills"] = new JsonArray(Alias("VariableID:1:1")),
        });

        var after = Node("1:1", "Card", boundVariables: new JsonObject
        {
            ["fills"] = new JsonArray(Alias("VariableID:1:2")),
        });

        var changes = DiffEngine.CollectBindingChanges(before, after);

        Assert.Single(changes);
        Assert.Equal("fills[0]", changes[0]!["property"]!.GetValue<string>());
        Assert.Equal("rebound", changes[0]!["change_kind"]!.GetValue<string>());
    }

    [Fact]
    public void UnchangedBindingsProduceNoNoise()
    {
        var node = Node("1:1", "Card", boundVariables: new JsonObject { ["fills"] = new JsonArray(Alias("VariableID:1:1")) });

        Assert.Empty(DiffEngine.CollectBindingChanges(node, node.DeepClone()));
    }

    [Fact]
    public void MissingSideIsReportedAsAppearedOrDisappearedNotAsAPileOfChanges()
    {
        var node = Node("1:1", "Button");

        var appeared = DiffEngine.DiffNode(null, node);
        Assert.Equal(1, appeared["change_count"]!.GetValue<int>());
        Assert.Contains("appeared", appeared["notes"]![0]!.GetValue<string>());

        var disappeared = DiffEngine.DiffNode(node, null);
        Assert.Equal(1, disappeared["change_count"]!.GetValue<int>());
        Assert.Contains("disappeared", disappeared["notes"]![0]!.GetValue<string>());

        var nowhere = DiffEngine.DiffNode(null, null);
        Assert.Equal(0, nowhere["change_count"]!.GetValue<int>());
    }

    [Fact]
    public void ChangeCountSumsEveryKindOfChange()
    {
        var before = Node(
            "1:1",
            "Button",
            description: "old",
            properties: new JsonObject { ["Disabled#1:2"] = Property("BOOLEAN", false) },
            children: new JsonArray(Node("1:2", "Default", "COMPONENT")));

        var after = Node(
            "1:1",
            "Submit",
            description: "new description",
            properties: new JsonObject
            {
                ["Disabled#1:2"] = Property("BOOLEAN", false),
                ["Size#1:3"] = Property("VARIANT", "M"),
            },
            children: new JsonArray(Node("1:3", "Hover", "COMPONENT")),
            boundVariables: new JsonObject { ["fills"] = new JsonArray(Alias("VariableID:1:1")) });

        var diff = DiffEngine.DiffNode(before, after, DiffModes.Detailed);

        Assert.Equal(6, diff["change_count"]!.GetValue<int>());
        Assert.Equal("Submit", diff["node_name"]!.GetValue<string>());
        Assert.Equal(1, Count(diff["children_added"]));
        Assert.Equal(1, Count(diff["children_removed"]));
        Assert.Equal(1, Count(diff["binding_changes"]));
    }

    [Fact]
    public void SummaryModeDropsDetailsButKeepsTheCounts()
    {
        var before = Node("1:1", "Button", properties: new JsonObject { ["Disabled#1:2"] = Property("BOOLEAN", false) });
        var after = Node(
            "1:1",
            "Button",
            properties: new JsonObject
            {
                ["Disabled#1:2"] = Property("BOOLEAN", false),
                ["Size#1:3"] = Property("VARIANT", "M"),
            },
            children: new JsonArray(Node("1:4", "Hover", "COMPONENT")));

        var detailed = DiffEngine.DiffNode(before, after, DiffModes.Detailed);
        var summary = DiffEngine.DiffNode(before, after, DiffModes.Summary);

        Assert.Equal(detailed["change_count"]!.GetValue<int>(), summary["change_count"]!.GetValue<int>());
        Assert.Empty((JsonArray)summary["children_added"]!);
        Assert.Empty((JsonArray)summary["component_properties"]!["added"]!.AsArray());
        Assert.Equal(1, summary["component_properties"]!["summary"]!["added"]!.GetValue<int>());
    }

    [Fact]
    public void StandardModeKeepsPropertyNamesAndDropsHeavyDefaults()
    {
        var before = Node("1:1", "Button", properties: new JsonObject { ["Label#1:2"] = Property("TEXT", "Button") });
        var after = Node("1:1", "Button", properties: new JsonObject { ["Label#1:2"] = Property("TEXT", "Submit") });

        var standard = DiffEngine.DiffNode(before, after);
        var changed = standard["component_properties"]!["default_changed"]![0]!;

        Assert.Equal("Label#1:2", changed["name"]!.GetValue<string>());
        Assert.Null(changed["from_default"]);
        Assert.Null(changed["to_default"]);

        var detailed = DiffEngine.DiffNode(before, after, DiffModes.Detailed);
        Assert.Equal("Button", detailed["component_properties"]!["default_changed"]![0]!["from_default"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("detailed", "detailed")]
    [InlineData("SUMMARY", "summary")]
    [InlineData("verbose", "standard")]
    [InlineData(null, "standard")]
    public void UnknownModeFallsBackToStandard(string? input, string expected) =>
        Assert.Equal(expected, DiffModes.Normalize(input));

    private static ChangelogInput Input(
        JsonObject? pageStructure = null,
        IReadOnlyList<JsonObject>? scoped = null,
        IReadOnlyList<string>? notes = null,
        VersionMeta? from = null,
        VersionMeta? to = null) =>
        new()
        {
            FileKey = "abc123",
            FileName = "Design System",
            FromVersionId = "v1",
            ToVersionId = "v2",
            FromMeta = from ?? new VersionMeta("v1", "Release 1.0", "2026-01-01T00:00:00Z", "Ann"),
            ToMeta = to ?? new VersionMeta("v2", "Release 1.1", "2026-01-06T00:00:00Z", "Bob"),
            PageStructure = pageStructure ?? DiffEngine.DiffPageStructure(null, null),
            ScopedNodes = scoped,
            Notes = notes,
        };

    [Fact]
    public void ChangelogHeaderCarriesBothVersionsAndTheirAuthors()
    {
        var markdown = ChangelogFormatter.Render(Input());

        Assert.StartsWith("# Design System — changelog", markdown);
        Assert.Contains("'Release 1.0' — 2026-01-01 — by Ann `v1`", markdown);
        Assert.Contains("'Release 1.1' — 2026-01-06 — by Bob `v2`", markdown);
        Assert.Contains("**Days apart:** 5", markdown);
    }

    [Theory]
    [InlineData("2026-01-02T00:00:00Z", 1)]
    [InlineData("2026-01-01T18:00:00Z", 1)]
    [InlineData("2026-01-01T00:00:00Z", 0)]
    [InlineData("2025-12-25T00:00:00Z", 0)]
    public void TheSpanIsRoundedToWholeDaysAndNeverGoesNegative(string toDate, int expected)
    {
        var markdown = ChangelogFormatter.Render(Input(
            to: new VersionMeta("v2", "Release 1.1", toDate, "Bob")));

        Assert.Contains($"**Days apart:** {expected}", markdown);
    }

    [Fact]
    public void HeadIsRenderedAsCurrentStateNotAsAVersionId()
    {
        var markdown = ChangelogFormatter.Render(Input(
            to: new VersionMeta("v-head", null, "2026-01-06T00:00:00Z", null, IsHead: true)));

        Assert.Contains("Current state (last edit 2026-01-06)", markdown);
    }

    [Fact]
    public void ComponentsSectionSplitsChangedFromUnchanged()
    {
        var changed = DiffEngine.DiffNode(
            Node("1:1", "Button"),
            Node("1:1", "Button v2"));
        var untouched = DiffEngine.DiffNode(Node("2:1", "Input"), Node("2:1", "Input"));

        var markdown = ChangelogFormatter.Render(Input(scoped: [changed, untouched]));

        Assert.Contains("### Button v2 — `1:1`", markdown);
        Assert.Contains("**Changes: 1**", markdown);
        Assert.Contains("**Unchanged:** `Input`", markdown);
    }

    [Fact]
    public void MarkdownSpecialCharactersInLayerNamesAreEscaped()
    {
        var diff = DiffEngine.DiffNode(
            Node("1:1", "Button [old]"),
            Node("1:1", "Button *new*"));

        var markdown = ChangelogFormatter.Render(Input(scoped: [diff]));

        Assert.Contains(@"Button \*new\*", markdown);
        Assert.DoesNotContain("### Button *new*", markdown);
    }

    [Fact]
    public void SummaryModeStopsAfterOneLineAndKeepsTheCaveats()
    {
        var pages = DiffEngine.DiffPageStructure(
            Document(Page("0:1", "Cover")),
            Document(Page("0:1", "Cover"), Page("0:2", "Icons")));
        var diff = DiffEngine.DiffNode(Node("1:1", "Button"), Node("1:1", "Button v2"));

        var markdown = ChangelogFormatter.Render(
            Input(pageStructure: pages, scoped: [diff], notes: ["The plugin was not connected."]),
            DiffModes.Summary);

        Assert.Contains("Pages added: 1", markdown);
        Assert.Contains("Components changed: 1; Changes: 1.", markdown);
        Assert.DoesNotContain("## Page structure", markdown);
        Assert.Contains("## Caveats", markdown);
        Assert.Contains("The plugin was not connected.", markdown);
    }

    [Fact]
    public void EmptyScopeSaysHowToGetAComponentSection()
    {
        var markdown = ChangelogFormatter.Render(Input());

        Assert.Contains("_Nothing changed at the page level._", markdown);
        Assert.Contains("componentIds", markdown);
    }

    [Fact]
    public void MetadataChangesFromThePluginBufferAreLabelledAsSuch()
    {
        var diff = DiffEngine.DiffNode(Node("1:1", "Button"), Node("1:1", "Button v2"));
        diff["metadata_changes"] = new JsonArray(new JsonObject
        {
            ["field"] = "annotations",
            ["source"] = "plugin_buffer",
        });

        var markdown = ChangelogFormatter.Render(Input(scoped: [diff]), DiffModes.Detailed);

        Assert.Contains("from the plugin buffer", markdown);
        Assert.Contains("annotations changed", markdown);
    }

    [Fact]
    public void HeadGetsNoCacheKeyBecauseItChangesWithEverySave()
    {
        Assert.Null(VersionSnapshotCache.MakeKey("abc", null, 1, null));
        Assert.Null(VersionSnapshotCache.MakeKey("abc", "  ", 1, null));
        Assert.NotNull(VersionSnapshotCache.MakeKey("abc", "v1", 1, null));
    }

    [Fact]
    public void NodeOrderDoesNotChangeTheKeyButTheNodeSetDoes()
    {
        var straight = VersionSnapshotCache.MakeKey("abc", "v1", 2, ["1:1", "1:2"]);
        var reversed = VersionSnapshotCache.MakeKey("abc", "v1", 2, ["1:2", "1:1"]);
        var narrower = VersionSnapshotCache.MakeKey("abc", "v1", 2, ["1:1"]);
        var deeper = VersionSnapshotCache.MakeKey("abc", "v1", 3, ["1:1", "1:2"]);

        Assert.Equal(straight, reversed);
        Assert.NotEqual(straight, narrower);
        Assert.NotEqual(straight, deeper);
    }

    [Fact]
    public void WritingWithoutAKeyIsANoOpSoHeadNeverLandsInTheCache()
    {
        var cache = new VersionSnapshotCache();

        cache.Set(null, new JsonObject { ["document"] = "head" });

        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Get(null));
    }

    [Fact]
    public void OldestUntouchedEntryIsEvictedFirst()
    {
        var cache = new VersionSnapshotCache(maxEntries: 2);

        cache.Set("a", new JsonObject { ["n"] = 1 });
        cache.Set("b", new JsonObject { ["n"] = 2 });

        Assert.NotNull(cache.Get("a"));

        cache.Set("c", new JsonObject { ["n"] = 3 });

        Assert.Equal(2, cache.Count);
        Assert.NotNull(cache.Get("a"));
        Assert.NotNull(cache.Get("c"));
        Assert.Null(cache.Get("b"));
    }

    [Fact]
    public void RewritingAKeyDoesNotGrowTheCache()
    {
        var cache = new VersionSnapshotCache(maxEntries: 2);

        cache.Set("a", new JsonObject { ["n"] = 1 });
        cache.Set("a", new JsonObject { ["n"] = 2 });

        Assert.Equal(1, cache.Count);
        Assert.Equal(2, cache.Get("a")!["n"]!.GetValue<int>());
    }
}
