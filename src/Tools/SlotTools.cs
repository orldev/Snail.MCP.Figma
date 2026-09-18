using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Figma slots (open beta): content zones more flexible than INSTANCE_SWAP, filled via figma_append_to_slot.</summary>
[McpServerToolType]
public sealed class SlotTools(ILiveEditor plugin, ILogger<SlotTools> logger)
    : EditorToolBase(plugin, logger)
{
    [McpServerTool(Name = "figma_create_slot", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Slot.CreateSlot)]
    public Task<string> CreateSlotAsync(
        [Description("Id of the COMPONENT node to add the slot to.")] string nodeId,
        [Description("Slot layer name, for example 'Content' or 'Footer'.")] string? name = null,
        [Description("Initial width in pixels.")] double? width = null,
        [Description("Initial height in pixels.")] double? height = null,
        [Description("Auto layout mode of the slot: NONE, HORIZONTAL, VERTICAL. GRID is not supported.")] string? layoutMode = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CreateSlot, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["name"] = name,
            ["width"] = width,
            ["height"] = height,
            ["layoutMode"] = layoutMode,
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_slots", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Slot.GetSlots)]
    public Task<string> GetSlotsAsync(
        [Description("Id of the COMPONENT, COMPONENT_SET or INSTANCE node.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetSlots, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
        }, cancellationToken);

    [McpServerTool(Name = "figma_append_to_slot", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Slot.AppendToSlot)]
    public Task<string> AppendToSlotAsync(
        [Description("SlotNode id — from figma_get_slots.")] string? slotId = null,
        [Description("Instance id. Works together with slotName.")] string? instanceId = null,
        [Description("Slot layer name on the instance, for example 'Content'.")] string? slotName = null,
        [Description("The node to clone into the slot.")] string? sourceNodeId = null,
        [Description("Type of the new node instead of cloning.")] string? nodeType = null,
        [Description("Properties of the node being created as JSON: {\"name\":\"…\",\"text\":\"…\",\"width\":100}.")] string? properties = null,
        [Description("With sourceNodeId: clone (true) or move (false).")] bool clone = true,
        [Description("Delete the previous slot contents before inserting.")] bool clearExisting = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(slotId) && (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(slotName)))
            {
                return ToolResponse.Error(
                    "The slot is not addressed.",
                    "Pass slotId or the pair instanceId + slotName. figma_get_slots shows the slot names.");
            }

            if (string.IsNullOrWhiteSpace(sourceNodeId) && string.IsNullOrWhiteSpace(nodeType))
            {
                return ToolResponse.Error(
                    "It is not stated what to put into the slot.",
                    "Pass sourceNodeId (clone an existing node) or nodeType (create a new one).");
            }

            if (!TryParseJson(properties, out var parsedProperties, out var error))
            {
                return ToolResponse.Error($"properties did not parse as JSON: {error}");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.AppendToSlot, new JsonObject
            {
                ["slotId"] = slotId is null ? null : NodeId.Normalize(slotId),
                ["instanceId"] = instanceId is null ? null : NodeId.Normalize(instanceId),
                ["slotName"] = slotName,
                ["sourceNodeId"] = sourceNodeId is null ? null : NodeId.Normalize(sourceNodeId),
                ["nodeType"] = nodeType,
                ["properties"] = parsedProperties?.DeepClone(),
                ["clone"] = clone,
                ["clearExisting"] = clearExisting,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_reset_slot", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Slot.ResetSlot)]
    public Task<string> ResetSlotAsync(
        [Description("SlotNode id.")] string? slotId = null,
        [Description("Instance id. Works together with slotName.")] string? instanceId = null,
        [Description("Slot layer name on the instance.")] string? slotName = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(slotId) && (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(slotName)))
            {
                return ToolResponse.Error(
                    "The slot is not addressed.",
                    "Pass slotId or the pair instanceId + slotName.");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.ResetSlot, new JsonObject
            {
                ["slotId"] = slotId is null ? null : NodeId.Normalize(slotId),
                ["instanceId"] = instanceId is null ? null : NodeId.Normalize(instanceId),
                ["slotName"] = slotName,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_add_slot_property", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Slot.AddSlotProperty)]
    public Task<string> AddSlotPropertyAsync(
        [Description("Id of the COMPONENT or COMPONENT_SET node.")] string nodeId,
        [Description("Slot property name, for example 'Content'.")] string propertyName,
        [Description("Id of the frame that becomes the content area of the slot.")] string frameNodeId,
        [Description("Description of the slot property.")] string? description = null,
        [Description("Preferred components for the contents, a JSON array.")] string? preferredValues = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(preferredValues, out var parsedPreferred, out var error))
            {
                return ToolResponse.Error(
                    $"preferredValues did not parse as JSON: {error}",
                    "Expected [{\"type\":\"COMPONENT\",\"key\":\"…\"}].");
            }

            var component = NodeId.Normalize(nodeId);
            var frame = NodeId.Normalize(frameNodeId);

            var options = new JsonObject();

            if (!string.IsNullOrWhiteSpace(description))
            {
                options["description"] = description;
            }

            if (parsedPreferred is not null)
            {
                options["preferredValues"] = parsedPreferred.DeepClone();
            }

            var parameters = new JsonObject
            {
                ["componentId"] = component,
                ["frameId"] = frame,
                ["propertyName"] = propertyName,
                ["options"] = options,
            };

            var code = $$"""
                const params = JSON.parse({{JsonSerializer.Serialize(parameters.ToJsonString())}});
                const component = await figma.getNodeByIdAsync(params.componentId);
                if (!component || (component.type !== 'COMPONENT' && component.type !== 'COMPONENT_SET')) {
                  throw new Error('Node must be COMPONENT or COMPONENT_SET');
                }
                const frame = await figma.getNodeByIdAsync(params.frameId);
                if (!frame || frame.type !== 'FRAME') {
                  throw new Error('frameNodeId must be a FRAME node');
                }
                // For COMPONENT the frame must be a direct child. For COMPONENT_SET the property lives on the set
                // and the frame sits inside a variant — accept a frame whose parent is a variant of the set.
                const directChild = frame.parent === component;
                const variantChild = component.type === 'COMPONENT_SET' &&
                  frame.parent && frame.parent.type === 'COMPONENT' && frame.parent.parent === component;
                if (!directChild && !variantChild) {
                  throw new Error(component.type === 'COMPONENT_SET'
                    ? 'Frame must be a direct child of one of the set variants'
                    : 'Frame must be a direct child of the component');
                }
                if (frame.layoutMode === 'GRID') {
                  throw new Error('GRID layoutMode is not allowed on slot frames');
                }
                const options = params.options && Object.keys(params.options).length ? params.options : undefined;
                const propKey = component.addComponentProperty(params.propertyName, 'SLOT', '', options);
                // Merge on purpose: assigning a new object would wipe the frame's existing bindings,
                // such as a BOOLEAN property driving visible.
                frame.componentPropertyReferences = Object.assign({}, frame.componentPropertyReferences, { slotContentId: propKey });
                return { propertyKey: propKey, frameId: frame.id, frameName: frame.name };
                """;

            var result = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject
                {
                    ["code"] = code,
                    ["timeout"] = 10000,
                    ["nodeIds"] = new JsonArray(component, frame),
                },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);
}
