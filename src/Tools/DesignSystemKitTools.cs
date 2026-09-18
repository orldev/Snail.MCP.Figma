using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Extraction;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Tools;

/// <summary>Design-system kit in one call: tokens, components and styles, with delta-encoded variant specs and tiered compression.</summary>
[McpServerToolType]
public sealed class DesignSystemKitTools(
    ILiveEditor plugin,
    IPublishedDocumentSource api,
    DocumentRenderer images,
    ActiveDocumentTracker activeFile,
    ILogger<DesignSystemKitTools> logger)
    : EditorToolBase(plugin, logger)
{
    private const int BatchSize = 50;

    [McpServerTool(Name = "figma_get_design_system_kit", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.DesignSystemKit.GetDesignSystemKit)]
    public Task<string> GetDesignSystemKitAsync(
        [Description("Link to the Figma file or its key. Optional if the file has already been used.")] string? fileUrl = null,
        [Description("Which sections to include, comma-separated: tokens, components, styles. Empty means all three.")] string? include = null,
        [Description("Comma-separated ids of specific components. Empty means every published one.")] string? componentIds = null,
        [Description("Add links to component images. Noticeably slower.")] bool includeImages = false,
        [Description("Response size: full is everything, summary drops variant specifications, compact keeps only names, types and properties. It is compressed further automatically when the response is too large.")] string format = "full",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);
            var sections = ParseSections(include);
            var errors = new JsonArray();

            var kit = new JsonObject
            {
                ["fileKey"] = fileKey,
                ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["format"] = format,
            };

            if (sections.Contains("tokens"))
            {
                await AddTokensAsync(kit, errors, fileUrl, fileKey, ct).ConfigureAwait(false);
            }

            if (sections.Contains("components"))
            {
                await AddComponentsAsync(kit, errors, fileKey, ParseIds(componentIds), includeImages, ct)
                    .ConfigureAwait(false);
            }

            if (sections.Contains("styles"))
            {
                await AddStylesAsync(kit, errors, fileKey, ct).ConfigureAwait(false);
            }

            if (errors.Count > 0)
            {
                kit["errors"] = errors;
            }

            kit["ai_instruction"] = DesignSystemKit.AiInstruction(DescribeSections(kit));

            var sizeKb = DesignSystemKit.SizeKb(kit);
            var level = DesignSystemKit.CompressionLevel(format, sizeKb);

            if (level is null)
            {
                return ToolResponse.Ok(kit);
            }

            var compressed = DesignSystemKit.Compress(kit, level);
            var compressedKb = DesignSystemKit.SizeKb(compressed);

            if (sizeKb > 100)
            {
                compressed["ai_instruction"] =
                    $"The response was compressed automatically (level {level}) from {DesignSystemKit.FormatKb(sizeKb)} KB to " +
                    $"{DesignSystemKit.FormatKb(compressedKb)} KB. {compressed["ai_instruction"]?.GetValue<string>()} " +
                    "For the full specifications of specific components call the tool again with componentIds and format='full'.";
            }

            Logger.LogInformation(
                "The design system kit was compressed: {Original} KB → {Compressed} KB, level {Level}",
                DesignSystemKit.FormatKb(sizeKb),
                DesignSystemKit.FormatKb(compressedKb),
                level);

            return ToolResponse.Ok(compressed);
        }, cancellationToken);

    private async Task AddTokensAsync(
        JsonObject kit,
        JsonArray errors,
        string? fileUrl,
        string fileKey,
        CancellationToken cancellationToken)
    {
        try
        {
            JsonNode? raw = null;

            if (Plugin.IsConnected)
            {
                try
                {
                    raw = await Plugin.ReadAsync(
                        EditorCommands.GetVariablesData,
                        timeout: TimeSpan.FromSeconds(45),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is EditorCommandException or TimeoutException or EditorUnavailableException)
                {
                    Logger.LogInformation("Plugin did not return variables, trying REST: {Message}", ex.Message);
                }
            }

            raw ??= await api.GetLocalVariablesAsync(fileKey, cancellationToken).ConfigureAwait(false);

            var formatted = VariableFormatter.Format(raw);
            kit["tokens"] = new JsonObject
            {
                ["collections"] = GroupVariablesByCollection(formatted),
                ["summary"] = formatted["summary"]?.DeepClone(),
            };
        }
        catch (Exception ex) when (ex is PublishedSourceException or EditorCommandException or TimeoutException or EditorUnavailableException)
        {
            Logger.LogWarning("Tokens not received: {Message}", ex.Message);
            errors.Add(new JsonObject { ["section"] = "tokens", ["message"] = ex.Message });
        }
    }

    /// <summary>Variables grouped by collection — far easier to read than a flat list.</summary>
    private static JsonArray GroupVariablesByCollection(JsonObject formatted)
    {
        var result = new JsonArray();
        var variables = formatted["variables"] as JsonArray ?? [];

        foreach (var collection in formatted["collections"] as JsonArray ?? [])
        {
            var id = collection?["id"]?.GetValue<string>();

            var own = new JsonArray(variables
                .Where(variable => variable?["variableCollectionId"]?.GetValue<string>() == id)
                .Select(variable => (JsonNode)new JsonObject
                {
                    ["id"] = variable?["id"]?.DeepClone(),
                    ["name"] = variable?["name"]?.DeepClone(),
                    ["type"] = variable?["resolvedType"]?.DeepClone(),
                    ["description"] = variable?["description"]?.DeepClone(),
                    ["valuesByMode"] = variable?["valuesByMode"]?.DeepClone(),
                    ["scopes"] = variable?["scopes"]?.DeepClone(),
                })
                .ToArray());

            result.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = collection?["name"]?.DeepClone(),
                ["modes"] = collection?["modes"]?.DeepClone(),
                ["variables"] = own,
            });
        }

        return result;
    }

    private async Task AddComponentsAsync(
        JsonObject kit,
        JsonArray errors,
        string fileKey,
        IReadOnlyList<string> componentIds,
        bool includeImages,
        CancellationToken cancellationToken)
    {
        try
        {
            var componentsTask = api.GetComponentsAsync(fileKey, cancellationToken);
            var setsTask = api.GetComponentSetsAsync(fileKey, cancellationToken);
            await Task.WhenAll(componentsTask, setsTask).ConfigureAwait(false);

            var allComponents = (await componentsTask.ConfigureAwait(false))["meta"]?["components"] as JsonArray ?? [];
            var allSets = (await setsTask.ConfigureAwait(false))["meta"]?["component_sets"] as JsonArray ?? [];

            var setNodeIds = new HashSet<string>(
                allSets.Select(set => set?["node_id"]?.GetValue<string>()).OfType<string>(),
                StringComparer.Ordinal);

            var standalone = allComponents.Where(component =>
            {
                var frame = component?["containing_frame"];

                if (frame?["containingComponentSet"] is null)
                {
                    return true;
                }

                var frameNodeId = frame["nodeId"]?.GetValue<string>();
                var setNodeId = frame["containingComponentSet"]?["nodeId"]?.GetValue<string>();

                return (frameNodeId is null || !setNodeIds.Contains(frameNodeId))
                    && (setNodeId is null || !setNodeIds.Contains(setNodeId));
            }).ToList();

            var targetSets = allSets.ToList();
            var targetComponents = standalone;

            if (componentIds.Count > 0)
            {
                var wanted = new HashSet<string>(componentIds, StringComparer.Ordinal);
                targetComponents = standalone
                    .Where(c => c?["node_id"]?.GetValue<string>() is { } id && wanted.Contains(id))
                    .ToList();
                targetSets = allSets
                    .Where(s => s?["node_id"]?.GetValue<string>() is { } id && wanted.Contains(id))
                    .ToList();
            }

            var nodeIds = targetSets.Concat(targetComponents)
                .Select(item => item?["node_id"]?.GetValue<string>())
                .OfType<string>()
                .ToList();

            var nodeDetails = await LoadNodeDetailsAsync(fileKey, nodeIds, cancellationToken).ConfigureAwait(false);

            var items = new JsonArray();

            foreach (var set in targetSets)
            {
                var setNodeId = set?["node_id"]?.GetValue<string>();
                var setNode = setNodeId is null ? null : nodeDetails.GetValueOrDefault(setNodeId);

                var spec = new JsonObject
                {
                    ["id"] = setNodeId,
                    ["name"] = set?["name"]?.DeepClone(),
                    ["description"] = NullIfEmpty(set?["description"]),
                };

                var variants = new JsonArray(allComponents
                    .Where(component =>
                        component?["component_set_id"]?.GetValue<string>() == setNodeId
                        || component?["containing_frame"]?["nodeId"]?.GetValue<string>() == setNodeId
                        || component?["containing_frame"]?["containingComponentSet"]?["nodeId"]?.GetValue<string>() == setNodeId)
                    .Select(component =>
                    {
                        var variantId = component?["node_id"]?.GetValue<string>();
                        var entry = new JsonObject
                        {
                            ["name"] = component?["name"]?.DeepClone(),
                            ["id"] = variantId,
                        };

                        var variantNode = (setNode?["children"] as JsonArray)?
                            .FirstOrDefault(child => child?["id"]?.GetValue<string>() == variantId);

                        if (DesignSystemKit.ExtractVisualSpec(variantNode) is { } variantSpec)
                        {
                            entry["visualSpec"] = variantSpec;
                        }

                        return (JsonNode)entry;
                    })
                    .ToArray());

                if (variants.Count > 0)
                {
                    DesignSystemKit.DeltaEncodeVariants(variants);
                    spec["variants"] = variants;
                }

                ApplyNodeFacts(spec, setNode);
                items.Add(spec);
            }

            foreach (var component in targetComponents)
            {
                var nodeId = component?["node_id"]?.GetValue<string>();
                var node = nodeId is null ? null : nodeDetails.GetValueOrDefault(nodeId);

                var spec = new JsonObject
                {
                    ["id"] = nodeId,
                    ["name"] = component?["name"]?.DeepClone(),
                    ["description"] = NullIfEmpty(component?["description"]),
                };

                ApplyNodeFacts(spec, node);
                items.Add(spec);
            }

            if (includeImages && items.Count > 0)
            {
                await AddImagesAsync(fileKey, items, errors, cancellationToken).ConfigureAwait(false);
            }

            kit["components"] = new JsonObject
            {
                ["items"] = items,
                ["summary"] = new JsonObject
                {
                    ["totalComponents"] = items.Count,
                    ["totalComponentSets"] = targetSets.Count,
                },
            };
        }
        catch (PublishedSourceException ex)
        {
            Logger.LogWarning("Components not received: {Message}", ex.Message);
            errors.Add(new JsonObject { ["section"] = "components", ["message"] = ex.Message });
        }
    }

    private static void ApplyNodeFacts(JsonObject spec, JsonNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (node["componentPropertyDefinitions"] is { } properties)
        {
            spec["properties"] = properties.DeepClone();
        }

        if (node["absoluteBoundingBox"] is { } bounds)
        {
            spec["bounds"] = new JsonObject
            {
                ["width"] = bounds["width"]?.DeepClone(),
                ["height"] = bounds["height"]?.DeepClone(),
            };
        }

        if (DesignSystemKit.ExtractVisualSpec(node) is { } visual)
        {
            spec["visualSpec"] = visual;
        }
    }

    private async Task<Dictionary<string, JsonNode>> LoadNodeDetailsAsync(
        string fileKey,
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        for (var offset = 0; offset < nodeIds.Count; offset += BatchSize)
        {
            var batch = nodeIds.Skip(offset).Take(BatchSize).ToList();

            try
            {
                var response = await api
                    .GetNodesAsync(fileKey, batch, new NodeReadOptions { Depth = 2 }, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var (nodeId, payload) in response["nodes"] as JsonObject ?? [])
                {
                    if (payload?["document"] is { } document)
                    {
                        details[nodeId] = document;
                    }
                }
            }
            catch (PublishedSourceException ex)
            {
                Logger.LogWarning("Node batch could not be read: {Message}", ex.Message);
            }
        }

        return details;
    }

    private async Task AddImagesAsync(
        string fileKey,
        JsonArray items,
        JsonArray errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var ids = items.Select(item => item?["id"]?.GetValue<string>()).OfType<string>().ToList();

            for (var offset = 0; offset < ids.Count; offset += BatchSize)
            {
                var batch = ids.Skip(offset).Take(BatchSize).ToList();

                var render = await images
                    .RenderAsync(fileKey, batch, format: "png", scale: 2, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var item in items)
                {
                    if (item?["id"]?.GetValue<string>() is { } id &&
                        render.Urls.TryGetValue(NodeId.Normalize(id), out var url))
                    {
                        item["imageUrl"] = url;
                    }
                }
            }
        }
        catch (PublishedSourceException ex)
        {
            Logger.LogWarning("Component images not received: {Message}", ex.Message);
            errors.Add(new JsonObject { ["section"] = "component_images", ["message"] = ex.Message });
        }
    }

    private async Task AddStylesAsync(
        JsonObject kit,
        JsonArray errors,
        string fileKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await api.GetStylesAsync(fileKey, cancellationToken).ConfigureAwait(false);
            var raw = response["meta"]?["styles"] as JsonArray ?? [];

            var items = new JsonArray(raw.Select(style => (JsonNode)new JsonObject
            {
                ["key"] = style?["key"]?.DeepClone(),
                ["name"] = style?["name"]?.DeepClone(),
                ["styleType"] = style?["style_type"]?.DeepClone(),
                ["description"] = NullIfEmpty(style?["description"]),
                ["nodeId"] = style?["node_id"]?.DeepClone(),
            }).ToArray());

            if (items.Count > 0)
            {
                var nodeIds = items.Select(style => style?["nodeId"]?.GetValue<string>()).OfType<string>().ToList();
                var nodes = await LoadNodeDetailsAsync(fileKey, nodeIds, cancellationToken).ConfigureAwait(false);

                foreach (var style in items)
                {
                    if (style?["nodeId"]?.GetValue<string>() is { } nodeId
                        && nodes.TryGetValue(nodeId, out var node)
                        && ResolveStyleValue(node) is { } value)
                    {
                        style["resolvedValue"] = value;
                    }
                }
            }

            var byType = new JsonObject();

            foreach (var group in items.GroupBy(style => style?["styleType"]?.GetValue<string>() ?? "UNKNOWN", StringComparer.Ordinal))
            {
                byType[group.Key] = group.Count();
            }

            kit["styles"] = new JsonObject
            {
                ["items"] = items,
                ["summary"] = new JsonObject
                {
                    ["totalStyles"] = items.Count,
                    ["stylesByType"] = byType,
                },
            };
        }
        catch (PublishedSourceException ex)
        {
            Logger.LogWarning("Styles not received: {Message}", ex.Message);
            errors.Add(new JsonObject { ["section"] = "styles", ["message"] = ex.Message });
        }
    }

    /// <summary>Real style value from its node: fill colors, typography, effects.</summary>
    private static JsonObject? ResolveStyleValue(JsonNode node)
    {
        var value = new JsonObject();

        if (node["fills"] is JsonArray fills)
        {
            value["fills"] = new JsonArray(fills
                .Where(fill => fill?["visible"]?.GetValue<bool>() != false)
                .Select(fill => (JsonNode)new JsonObject
                {
                    ["type"] = fill?["type"]?.DeepClone(),
                    ["color"] = fill?["color"] is { } color ? DesignSystemKit.RgbaToHex(color) : null,
                    ["opacity"] = fill?["opacity"]?.DeepClone(),
                })
                .ToArray());
        }

        if (node["type"]?.GetValue<string>() == "TEXT" && node["style"] is { } style)
        {
            value["typography"] = new JsonObject
            {
                ["fontFamily"] = style["fontFamily"]?.DeepClone(),
                ["fontSize"] = style["fontSize"]?.DeepClone(),
                ["fontWeight"] = style["fontWeight"]?.DeepClone(),
                ["lineHeight"] = style["lineHeightPx"]?.DeepClone(),
                ["letterSpacing"] = style["letterSpacing"]?.DeepClone(),
            };
        }

        if (node["effects"] is JsonArray effects)
        {
            value["effects"] = new JsonArray(effects
                .Where(effect => effect?["visible"]?.GetValue<bool>() != false)
                .Select(effect => (JsonNode)new JsonObject
                {
                    ["type"] = effect?["type"]?.DeepClone(),
                    ["color"] = effect?["color"] is { } color ? DesignSystemKit.RgbaToHex(color) : null,
                    ["offset"] = effect?["offset"]?.DeepClone(),
                    ["radius"] = effect?["radius"]?.DeepClone(),
                    ["spread"] = effect?["spread"]?.DeepClone(),
                })
                .ToArray());
        }

        return value.Count > 0 ? value : null;
    }

    private static JsonNode? NullIfEmpty(JsonNode? node) =>
        node?.GetValue<string>() is { Length: > 0 } text ? JsonValue.Create(text) : null;

    private static HashSet<string> ParseSections(string? include)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tokens", "components", "styles" };

        if (string.IsNullOrWhiteSpace(include))
        {
            return all;
        }

        var requested = include
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(section => all.Contains(section))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requested.Count > 0 ? requested : all;
    }

    private static List<string> ParseIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids)
            ? []
            : ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NodeId.Normalize)
                .ToList();

    private static List<string> DescribeSections(JsonObject kit)
    {
        var sections = new List<string>();

        if (kit["tokens"]?["summary"] is { } tokens)
        {
            sections.Add(
                $"{tokens["totalVariables"]?.ToJsonString() ?? "0"} tokens in "
                + $"{tokens["totalCollections"]?.ToJsonString() ?? "0"} collections");
        }

        if (kit["components"]?["summary"] is { } components)
        {
            sections.Add(
                $"{components["totalComponents"]?.ToJsonString() ?? "0"} components "
                + $"({components["totalComponentSets"]?.ToJsonString() ?? "0"} sets)");
        }

        if (kit["styles"]?["summary"] is { } styles)
        {
            sections.Add($"{styles["totalStyles"]?.ToJsonString() ?? "0"} styles");
        }

        return sections;
    }
}
