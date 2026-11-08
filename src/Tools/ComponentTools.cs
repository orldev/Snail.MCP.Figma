using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Application.Libraries;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Figma.Tools;

/// <summary>Components: instances, variant sets and component properties.</summary>
[McpServerToolType]
public sealed class ComponentTools(ILiveEditor plugin, PublishedLibrary library, ILogger<ComponentTools> logger)
    : EditorToolBase(plugin, logger)
{
    /// <summary>Building a large variant set is one uninterruptible pass inside the plugin.</summary>
    private static TimeSpan ComponentSetTimeout(int variantCount) =>
        TimeSpan.FromMilliseconds(Math.Clamp(variantCount * 1200, 35_000, 125_000));

    /// <summary>Finds components by name: in a library file through REST when a link is given, otherwise in the open file through the plugin, together with the library components the file already uses.</summary>
    [McpServerTool(Name = "figma_search_components", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Component.SearchComponents)]
    public Task<string> SearchComponentsAsync(
        [Description("Substring of the name or of the set a variant belongs to; in a library also of the description or page. Empty returns everything.")] string? query = null,
        [Description("Link to a library file, or its key. Given, the tool lists what that library publishes, with keys, through REST and without the plugin. Empty: the open file through the plugin.")] string? libraryFileUrl = null,
        [Description("With libraryFileUrl: also list every variant under its set, each with its own key.")] bool includeVariants = false,
        [Description("Maximum number of results.")] int limit = PublishedLibrary.DefaultLimit,
        [Description("With libraryFileUrl: how many matches to skip, the nextOffset of the previous answer.")] int offset = 0,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(libraryFileUrl))
            {
                var answer = await Plugin.ReadAsync(EditorCommands.GetLocalComponents, OpenFileComponents.Request(), timeout: OpenFileComponents.Timeout, cancellationToken: ct)
                    .ConfigureAwait(false);

                return ToolAnswer.OkText(OpenFileComponents.Match(answer, query, Math.Max(1, limit)));
            }

            if (PublishedLibrary.FileKeyOf(libraryFileUrl) is not { } fileKey)
            {
                return ToolAnswer.FailedText(
                    $"Could not read a file key from '{libraryFileUrl}'.",
                    Messages.LibraryFileLinkHint);
            }

            var page = await library.ReadAsync(new LibraryQuery(fileKey, query, includeVariants, offset, limit), ct).ConfigureAwait(false);

            var hint = page switch
            {
                { IsEmptyLibrary: true } => Messages.NothingPublishedHint,
                { NextOffset: { } next } => $"More matches: call again with offset={next}.",
                _ => null,
            };

            return ToolAnswer.OkText(page.Describe(), hint: hint);
        }, cancellationToken);

    /// <summary>Places an instance of a component, addressed by key or node id, through the plugin.</summary>
    [McpServerTool(Name = "figma_instantiate_component", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Component.InstantiateComponent)]
    public Task<string> InstantiateComponentAsync(
        [Description("Component key from a library.")] string? componentKey = null,
        [Description("Id of the component node in the current file.")] string? nodeId = null,
        [Description("Id of the parent to put the instance into.")] string? parentId = null,
        [Description("Position as JSON {\"x\":0,\"y\":0}.")] string? position = null,
        [Description("Instance property values as JSON.")] string? overrides = null,
        [Description("Set variant as JSON, for example {\"Size\":\"Large\"}.")] string? variant = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(componentKey) && string.IsNullOrWhiteSpace(nodeId))
            {
                return ToolAnswer.FailedText(
                    "Either componentKey or nodeId is required.",
                    "Find the component through figma_search_components and take its key or id from there.");
            }

            var okPosition = TryParseJson(position, out var parsedPosition, out var positionError);
            var okOverrides = TryParseJson(overrides, out var parsedOverrides, out var overridesError);
            var okVariant = TryParseJson(variant, out var parsedVariant, out var variantError);

            if (!okPosition || !okOverrides || !okVariant)
            {
                return ToolAnswer.FailedText(
                    $"A JSON parameter did not parse: {positionError ?? overridesError ?? variantError}");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.InstantiateComponent, new JsonObject
            {
                ["componentKey"] = componentKey ?? string.Empty,
                ["nodeId"] = nodeId is null ? null : NodeId.Normalize(nodeId),
                ["parentId"] = parentId is null ? null : NodeId.Normalize(parentId),
                ["position"] = parsedPosition,
                ["overrides"] = parsedOverrides,
                ["variant"] = parsedVariant,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolAnswer.OkText(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    /// <summary>Builds a variant set from existing components or from a property matrix through the plugin.</summary>
    [McpServerTool(Name = "figma_create_component_set", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Component.CreateComponentSet)]
    public Task<string> CreateComponentSetAsync(
        [Description("Set name.")] string name,
        [Description("Component ids as a JSON array.")] string? componentIds = null,
        [Description("Property matrix as JSON.")] string? properties = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var okIds = TryParseJson(componentIds, out var parsedIds, out var idsError);
            var okProperties = TryParseJson(properties, out var parsedProperties, out var propertiesError);

            if (!okIds || !okProperties)
            {
                return ToolAnswer.FailedText($"A JSON parameter did not parse: {idsError ?? propertiesError}");
            }

            var variantCount = CountVariants(parsedIds, parsedProperties);

            var result = await Plugin.ApplyAsync(
                EditorCommands.CreateComponentSet,
                new JsonObject
                {
                    ["name"] = name,
                    ["componentIds"] = parsedIds,
                    ["properties"] = parsedProperties,
                },
                timeout: ComponentSetTimeout(variantCount),
                cancellationToken: ct).ConfigureAwait(false);

            return ToolAnswer.OkText(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    /// <summary>Reports the property axes of a variant set and which combinations are present or missing.</summary>
    [McpServerTool(Name = "figma_analyze_component_set", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Component.AnalyzeComponentSet)]
    public Task<string> AnalyzeComponentSetAsync(
        [Description("Id of the COMPONENT_SET node.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.AnalyzeComponentSet,
            new JsonObject { ["nodeId"] = NodeId.Normalize(nodeId) }, cancellationToken);

    /// <summary>Lays the variants of a set out in a grid by their first two property axes through the plugin.</summary>
    [McpServerTool(Name = "figma_arrange_component_set", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Component.ArrangeComponentSet)]
    public Task<string> ArrangeComponentSetAsync(
        [Description("Id of the COMPONENT_SET node.")] string nodeId,
        [Description("Horizontal gap between variants.")] double columnGap = 24,
        [Description("Vertical gap between variants.")] double rowGap = 24,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var code = $$"""
                const set = await figma.getNodeByIdAsync('{{NodeId.Normalize(nodeId)}}');
                if (!set || set.type !== 'COMPONENT_SET') {
                  throw new Error('The node is not a COMPONENT_SET');
                }
                const variants = set.children.slice();
                const axisValues = new Map();
                for (const v of variants) {
                  for (const part of v.name.split(',')) {
                    const [axis, value] = part.split('=').map(s => s.trim());
                    if (!axis) continue;
                    if (!axisValues.has(axis)) axisValues.set(axis, []);
                    const list = axisValues.get(axis);
                    if (!list.includes(value)) list.push(value);
                  }
                }
                const axes = Array.from(axisValues.keys());
                const rowAxis = axes[0];
                const colAxis = axes[1];
                const indexOf = (variant, axis) => {
                  if (!axis) return 0;
                  const part = variant.name.split(',').map(s => s.trim())
                    .find(p => p.startsWith(axis + '='));
                  const value = part ? part.split('=')[1].trim() : '';
                  return Math.max(0, axisValues.get(axis).indexOf(value));
                };
                const cellWidth = Math.max(...variants.map(v => v.width)) + {{columnGap}};
                const cellHeight = Math.max(...variants.map(v => v.height)) + {{rowGap}};
                for (const v of variants) {
                  v.x = indexOf(v, colAxis) * cellWidth;
                  v.y = indexOf(v, rowAxis) * cellHeight;
                }
                return { arranged: variants.length, rowAxis: rowAxis || null, columnAxis: colAxis || null };
                """;

            var result = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = code, ["timeout"] = 25000, ["nodeId"] = NodeId.Normalize(nodeId) },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: ct).ConfigureAwait(false);

            return ToolAnswer.OkText(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    /// <summary>Adds a text, boolean, instance-swap, variant or slot property to a component through the plugin.</summary>
    [McpServerTool(Name = "figma_add_component_property", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Component.AddComponentProperty)]
    public Task<string> AddComponentPropertyAsync(
        [Description("Id of the component or variant set.")] string nodeId,
        [Description("Property name, for example 'Label'.")] string propertyName,
        [Description("Type: TEXT, BOOLEAN, INSTANCE_SWAP, VARIANT, SLOT.")] string type,
        [Description("Default value. For BOOLEAN it is true/false, for VARIANT the variant name.")] string? defaultValue = null,
        [Description("Comma-separated value options for VARIANT.")] string? variantOptions = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var normalizedType = type.ToUpperInvariant();

            if (normalizedType != "SLOT" && string.IsNullOrWhiteSpace(defaultValue) && normalizedType != "BOOLEAN")
            {
                return ToolAnswer.FailedText(
                    $"A property of type {normalizedType} needs a default value.",
                    normalizedType switch
                    {
                        "INSTANCE_SWAP" => "Pass a component id.",
                        "VARIANT" => "Pass a non-empty variant name.",
                        _ => "Pass a value in defaultValue.",
                    });
            }

            var parameters = new JsonObject
            {
                ["nodeId"] = NodeId.Normalize(nodeId),
                ["propertyName"] = propertyName,
                ["propertyType"] = normalizedType,
                ["defaultValue"] = normalizedType switch
                {
                    "SLOT" => JsonValue.Create(string.Empty),
                    "BOOLEAN" => JsonValue.Create(bool.TryParse(defaultValue, out var flag) && flag),
                    _ => JsonValue.Create(defaultValue ?? string.Empty),
                },
            };

            if (!string.IsNullOrWhiteSpace(variantOptions))
            {
                parameters["variantOptions"] = new JsonArray(variantOptions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(option => (JsonNode)JsonValue.Create(option)!)
                    .ToArray());
            }

            var result = await Plugin.ApplyAsync(EditorCommands.AddComponentProperty, parameters,
                cancellationToken: ct).ConfigureAwait(false);

            return ToolAnswer.OkText(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    /// <summary>Changes the default value of a component property through the plugin.</summary>
    [McpServerTool(Name = "figma_edit_component_property", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Component.EditComponentProperty)]
    public Task<string> EditComponentPropertyAsync(
        [Description("Component id.")] string nodeId,
        [Description("Property name.")] string propertyName,
        [Description("New default value.")] string newValue,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.EditComponentProperty, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["propertyName"] = propertyName,
            ["newValue"] = newValue,
        }, cancellationToken);

    /// <summary>Removes a property from a component through the plugin.</summary>
    [McpServerTool(Name = "figma_delete_component_property", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Component.DeleteComponentProperty)]
    public Task<string> DeleteComponentPropertyAsync(
        [Description("Component id.")] string nodeId,
        [Description("Property name.")] string propertyName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.DeleteComponentProperty, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["propertyName"] = propertyName,
        }, cancellationToken);

    /// <summary>Reads the live state of a component from the plugin, optionally as a deep tree.</summary>
    [McpServerTool(Name = "figma_get_component_details", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Component.GetComponentDetails)]
    public Task<string> GetComponentDetailsAsync(
        [Description("Node id.")] string nodeId,
        [Description("Deep breakdown with styles, tokens and states.")] bool deep = false,
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            deep ? EditorCommands.DeepGetComponent : EditorCommands.GetComponent,
            new JsonObject { ["nodeId"] = NodeId.Normalize(nodeId) },
            cancellationToken,
            TimeSpan.FromSeconds(45));

    /// <summary>The variant count decides how much the plugin builds, hence the timeout.</summary>
    internal static int CountVariants(JsonNode? componentIds, JsonNode? properties)
    {
        if (componentIds is JsonArray ids && ids.Count > 0)
        {
            return ids.Count;
        }

        if (properties is not JsonObject matrix)
        {
            return 1;
        }

        var total = 1;

        foreach (var (_, values) in matrix)
        {
            total *= Math.Max(1, (values as JsonArray)?.Count ?? 1);
        }

        return total;
    }
}
