namespace Snail.MCP.Figma.Application.Diff;

/// <summary>Verbosity of diff output.</summary>
public static class DiffModes
{
    public const string Summary = "summary";
    public const string Standard = "standard";
    public const string Detailed = "detailed";

    public static readonly IReadOnlyList<string> All = [Summary, Standard, Detailed];

    public static string Normalize(string? mode) =>
        mode is not null && All.Contains(mode, StringComparer.OrdinalIgnoreCase)
            ? mode.ToLowerInvariant()
            : Standard;
}

/// <summary>Pure diff functions over file and node snapshots of two versions; variable values and style contents are deliberately out of scope.</summary>
public static class DiffEngine
{
    /// <summary>Diffs two file documents, taking the <c>document</c> field of the Figma response.</summary>
    public static JsonObject DiffPageStructure(JsonNode? fromDocument, JsonNode? toDocument)
    {
        var fromPages = ExtractPages(fromDocument);
        var toPages = ExtractPages(toDocument);

        var added = new JsonArray();
        var removed = new JsonArray();
        var renamed = new JsonArray();

        foreach (var (id, name) in toPages)
        {
            if (!fromPages.TryGetValue(id, out var oldName))
            {
                added.Add(new JsonObject { ["id"] = id, ["name"] = name });
            }
            else if (!string.Equals(oldName, name, StringComparison.Ordinal))
            {
                renamed.Add(new JsonObject { ["id"] = id, ["old_name"] = oldName, ["new_name"] = name });
            }
        }

        foreach (var (id, name) in fromPages)
        {
            if (!toPages.ContainsKey(id))
            {
                removed.Add(new JsonObject { ["id"] = id, ["name"] = name });
            }
        }

        return new JsonObject
        {
            ["pages_added"] = added,
            ["pages_removed"] = removed,
            ["pages_renamed"] = renamed,
            ["summary"] = new JsonObject
            {
                ["added"] = added.Count,
                ["removed"] = removed.Count,
                ["renamed"] = renamed.Count,
            },
        };
    }

    private static Dictionary<string, string> ExtractPages(JsonNode? document)
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);

        if (document?["children"] is not JsonArray children)
        {
            return pages;
        }

        foreach (var child in children)
        {
            var id = child?["id"]?.GetValue<string>();
            var name = child?["name"]?.GetValue<string>();

            if (id is not null && name is not null)
            {
                pages[id] = name;
            }
        }

        return pages;
    }

    /// <summary>Diffs <c>componentPropertyDefinitions</c>; keys are <c>name#id</c>, so a rename reads as remove plus add.</summary>
    public static JsonObject DiffComponentPropertyDefinitions(JsonNode? fromDefinitions, JsonNode? toDefinitions)
    {
        var from = fromDefinitions as JsonObject ?? [];
        var to = toDefinitions as JsonObject ?? [];

        var added = new JsonArray();
        var removed = new JsonArray();
        var typeChanged = new JsonArray();
        var defaultChanged = new JsonArray();

        foreach (var (key, definition) in to)
        {
            if (!from.TryGetPropertyValue(key, out var previous))
            {
                added.Add(new JsonObject
                {
                    ["name"] = key,
                    ["type"] = definition?["type"]?.DeepClone(),
                    ["default_value"] = definition?["defaultValue"]?.DeepClone(),
                });

                continue;
            }

            var fromType = previous?["type"]?.GetValue<string>();
            var toType = definition?["type"]?.GetValue<string>();

            if (!string.Equals(fromType, toType, StringComparison.Ordinal))
            {
                typeChanged.Add(new JsonObject
                {
                    ["name"] = key,
                    ["from_type"] = fromType,
                    ["to_type"] = toType,
                });
            }

            if (!JsonEquals(previous?["defaultValue"], definition?["defaultValue"]))
            {
                defaultChanged.Add(new JsonObject
                {
                    ["name"] = key,
                    ["from_default"] = previous?["defaultValue"]?.DeepClone(),
                    ["to_default"] = definition?["defaultValue"]?.DeepClone(),
                });
            }
        }

        foreach (var (key, definition) in from)
        {
            if (!to.ContainsKey(key))
            {
                removed.Add(new JsonObject
                {
                    ["name"] = key,
                    ["type"] = definition?["type"]?.DeepClone(),
                    ["default_value"] = definition?["defaultValue"]?.DeepClone(),
                });
            }
        }

        return new JsonObject
        {
            ["added"] = added,
            ["removed"] = removed,
            ["type_changed"] = typeChanged,
            ["default_changed"] = defaultChanged,
            ["summary"] = new JsonObject
            {
                ["added"] = added.Count,
                ["removed"] = removed.Count,
                ["type_changed"] = typeChanged.Count,
                ["default_changed"] = defaultChanged.Count,
            },
        };
    }

    /// <summary>Walks both trees in parallel, matching nodes by id, and diffs top-level bindings.</summary>
    public static JsonArray CollectBindingChanges(JsonNode? fromNode, JsonNode? toNode)
    {
        var changes = new JsonArray();

        WalkBoth(fromNode, toNode, (a, b) =>
        {
            var before = ExtractTopLevelBindings(a);
            var after = ExtractTopLevelBindings(b);

            foreach (var property in before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal))
            {
                var fromId = before.GetValueOrDefault(property);
                var toId = after.GetValueOrDefault(property);

                if (string.Equals(fromId, toId, StringComparison.Ordinal))
                {
                    continue;
                }

                var kind = fromId is null ? "added" : toId is null ? "removed" : "rebound";
                var owner = b ?? a;

                changes.Add(new JsonObject
                {
                    ["node_id"] = owner?["id"]?.DeepClone(),
                    ["node_name"] = owner?["name"]?.GetValue<string>() ?? string.Empty,
                    ["property"] = property,
                    ["from_variable_id"] = fromId,
                    ["to_variable_id"] = toId,
                    ["change_kind"] = kind,
                });
            }
        });

        return changes;
    }

    private static Dictionary<string, string> ExtractTopLevelBindings(JsonNode? node)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (node?["boundVariables"] is not JsonObject bound)
        {
            return bindings;
        }

        foreach (var (property, reference) in bound)
        {
            if (reference is JsonObject single
                && single["type"]?.GetValue<string>() == "VARIABLE_ALIAS"
                && single["id"]?.GetValue<string>() is { } id)
            {
                bindings[property] = id;
                continue;
            }

            if (reference is JsonArray array)
            {
                var first = array.FirstOrDefault(item =>
                    item?["type"]?.GetValue<string>() == "VARIABLE_ALIAS" && item["id"] is not null);

                if (first?["id"]?.GetValue<string>() is { } firstId)
                {
                    bindings[$"{property}[0]"] = firstId;
                }
            }
        }

        return bindings;
    }

    private static void WalkBoth(JsonNode? fromNode, JsonNode? toNode, Action<JsonNode?, JsonNode?> visit)
    {
        if (fromNode is null && toNode is null)
        {
            return;
        }

        visit(fromNode, toNode);

        var fromChildren = ChildrenById(fromNode);
        var toChildren = ChildrenById(toNode);

        foreach (var id in fromChildren.Keys.Concat(toChildren.Keys).Distinct(StringComparer.Ordinal))
        {
            WalkBoth(fromChildren.GetValueOrDefault(id), toChildren.GetValueOrDefault(id), visit);
        }
    }

    private static Dictionary<string, JsonNode> ChildrenById(JsonNode? node)
    {
        var children = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        if (node?["children"] is not JsonArray array)
        {
            return children;
        }

        foreach (var child in array)
        {
            if (child?["id"]?.GetValue<string>() is { } id)
            {
                children[id] = child;
            }
        }

        return children;
    }

    /// <summary>Diffs one node, usually a COMPONENT_SET; a <c>null</c> side means the node appeared or disappeared.</summary>
    public static JsonObject DiffNode(JsonNode? fromNode, JsonNode? toNode, string mode = DiffModes.Standard)
    {
        var owner = toNode ?? fromNode;

        var result = new JsonObject
        {
            ["node_id"] = owner?["id"]?.GetValue<string>() ?? string.Empty,
            ["node_name"] = owner?["name"]?.GetValue<string>() ?? string.Empty,
            ["node_type"] = owner?["type"]?.GetValue<string>() ?? string.Empty,
            ["name_changed"] = null,
            ["description_changed"] = null,
            ["children_added"] = new JsonArray(),
            ["children_removed"] = new JsonArray(),
            ["component_properties"] = null,
            ["binding_changes"] = new JsonArray(),
            ["change_count"] = 0,
            ["notes"] = new JsonArray(),
        };

        var notes = result["notes"]!.AsArray();

        if (fromNode is null && toNode is not null)
        {
            notes.Add("The node appeared in the target version — there is nothing to compare with.");
            result["change_count"] = 1;

            return result;
        }

        if (fromNode is not null && toNode is null)
        {
            notes.Add("The node disappeared in the target version — there is nothing to compare with.");
            result["change_count"] = 1;

            return result;
        }

        if (fromNode is null || toNode is null)
        {
            notes.Add("The node was not found in either version.");

            return result;
        }

        var fromName = fromNode["name"]?.GetValue<string>() ?? string.Empty;
        var toName = toNode["name"]?.GetValue<string>() ?? string.Empty;

        if (!string.Equals(fromName, toName, StringComparison.Ordinal))
        {
            result["name_changed"] = new JsonObject { ["from"] = fromName, ["to"] = toName };
        }

        var fromDescription = fromNode["description"]?.GetValue<string>() ?? string.Empty;
        var toDescription = toNode["description"]?.GetValue<string>() ?? string.Empty;

        if (!string.Equals(fromDescription, toDescription, StringComparison.Ordinal))
        {
            result["description_changed"] = new JsonObject { ["from"] = fromDescription, ["to"] = toDescription };
        }

        var fromChildren = ChildrenById(fromNode);
        var toChildren = ChildrenById(toNode);
        var childrenAdded = result["children_added"]!.AsArray();
        var childrenRemoved = result["children_removed"]!.AsArray();

        foreach (var (id, child) in toChildren.Where(pair => !fromChildren.ContainsKey(pair.Key)))
        {
            childrenAdded.Add(ChildSummary(id, child));
        }

        foreach (var (id, child) in fromChildren.Where(pair => !toChildren.ContainsKey(pair.Key)))
        {
            childrenRemoved.Add(ChildSummary(id, child));
        }

        if (fromNode["type"]?.GetValue<string>() == "COMPONENT_SET"
            || toNode["type"]?.GetValue<string>() == "COMPONENT_SET")
        {
            var properties = DiffComponentPropertyDefinitions(
                fromNode["componentPropertyDefinitions"],
                toNode["componentPropertyDefinitions"]);

            if (SummaryTotal(properties["summary"]) > 0)
            {
                result["component_properties"] = properties;
            }
        }

        result["binding_changes"] = CollectBindingChanges(fromNode, toNode);

        result["change_count"] =
            (result["name_changed"] is null ? 0 : 1)
            + (result["description_changed"] is null ? 0 : 1)
            + childrenAdded.Count
            + childrenRemoved.Count
            + SummaryTotal(result["component_properties"]?["summary"])
            + result["binding_changes"]!.AsArray().Count;

        ApplyMode(result, mode);

        return result;
    }

    private static JsonObject ChildSummary(string id, JsonNode? child) => new()
    {
        ["id"] = id,
        ["name"] = child?["name"]?.GetValue<string>() ?? string.Empty,
        ["type"] = child?["type"]?.GetValue<string>() ?? string.Empty,
    };

    private static int SummaryTotal(JsonNode? summary) =>
        summary is not JsonObject obj
            ? 0
            : obj.Sum(pair => pair.Value?.GetValue<int>() ?? 0);

    /// <summary>The mode trims details but never changes counters, which are computed beforehand.</summary>
    private static void ApplyMode(JsonObject result, string mode)
    {
        if (mode == DiffModes.Detailed)
        {
            return;
        }

        if (mode == DiffModes.Summary)
        {
            result["children_added"] = new JsonArray();
            result["children_removed"] = new JsonArray();
            result["binding_changes"] = new JsonArray();

            if (result["component_properties"] is JsonObject properties)
            {
                result["component_properties"] = new JsonObject
                {
                    ["added"] = new JsonArray(),
                    ["removed"] = new JsonArray(),
                    ["type_changed"] = new JsonArray(),
                    ["default_changed"] = new JsonArray(),
                    ["summary"] = properties["summary"]?.DeepClone(),
                };
            }

            return;
        }

        if (result["component_properties"] is not JsonObject standard)
        {
            return;
        }

        result["component_properties"] = new JsonObject
        {
            ["added"] = StripDefaults(standard["added"]),
            ["removed"] = StripDefaults(standard["removed"]),
            ["type_changed"] = standard["type_changed"]?.DeepClone(),
            ["default_changed"] = new JsonArray((standard["default_changed"] as JsonArray ?? [])
                .Select(entry => (JsonNode)new JsonObject { ["name"] = entry?["name"]?.DeepClone() })
                .ToArray()),
            ["summary"] = standard["summary"]?.DeepClone(),
        };
    }

    private static JsonArray StripDefaults(JsonNode? entries) =>
        new((entries as JsonArray ?? [])
            .Select(entry => (JsonNode)new JsonObject
            {
                ["name"] = entry?["name"]?.DeepClone(),
                ["type"] = entry?["type"]?.DeepClone(),
            })
            .ToArray());

    private static bool JsonEquals(JsonNode? left, JsonNode? right) =>
        string.Equals(left?.ToJsonString(), right?.ToJsonString(), StringComparison.Ordinal);
}
