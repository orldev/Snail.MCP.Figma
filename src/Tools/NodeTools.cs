using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Node operations: clone, delete, rename, strokes, images, child creation.</summary>
[McpServerToolType]
public sealed class NodeTools(ILiveEditor plugin, IImageFiles images, ILogger<NodeTools> logger)
    : EditorToolBase(plugin, logger)
{
    /// <summary>Ceiling for a picture crossing the bridge.</summary>
    /// <remarks>Base64 is rebuilt into a byte array inside the plugin window, and megabytes take far longer there than the icons the old minute was measured on.</remarks>
    private static readonly TimeSpan ImageTransferTimeout = TimeSpan.FromMinutes(2);

    /// <summary>What Figma itself refuses, which is what the plugin's own message is usually about.</summary>
    /// <remarks>The ceiling in pixels lives in <see cref="ImageLimits.MaxPixelSide"/> and is kept out of this prose, so the two cannot drift apart.</remarks>
    private const string ImageRefusalHint =
        "A picture wider or taller than Figma's own ceiling fails inside the plugin as 'Image is too large' — crop it or export it smaller.";

    [McpServerTool(Name = "figma_clone_node", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Node.CloneNode)]
    public Task<string> CloneNodeAsync(
        [Description("Node id, for example '1:234'.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CloneNode,
            new JsonObject { ["nodeId"] = NodeId.Normalize(nodeId) }, cancellationToken);

    [McpServerTool(Name = "figma_delete_node", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Node.DeleteNode)]
    public Task<string> DeleteNodeAsync(
        [Description("Node id.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.DeleteNode,
            new JsonObject { ["nodeId"] = NodeId.Normalize(nodeId) }, cancellationToken);

    [McpServerTool(Name = "figma_rename_node", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Node.RenameNode)]
    public Task<string> RenameNodeAsync(
        [Description("Node id.")] string nodeId,
        [Description("New name.")] string newName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.RenameNode, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["newName"] = newName,
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_strokes", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Node.SetStrokes)]
    public Task<string> SetStrokesAsync(
        [Description("Node id.")] string nodeId,
        [Description("Array of Paint as a JSON string.")] string strokes,
        [Description("Stroke weight in pixels.")] double? strokeWeight = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(strokes, out var parsed, out var error) || parsed is null)
            {
                return ToolResponse.Error($"strokes did not parse as JSON: {error}",
                    "Expected [{\"type\":\"SOLID\",\"color\":{\"r\":0,\"g\":0,\"b\":0}}].");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.SetNodeStrokes, new JsonObject
            {
                ["nodeId"] = NodeId.Normalize(nodeId),
                ["strokes"] = parsed,
                ["strokeWeight"] = strokeWeight,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_image_fill", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Node.SetImageFill)]
    public Task<string> SetImageFillAsync(
        [Description("Node id, or several separated by commas.")] string nodeId,
        [Description("Image as base64. Pass either imageData or filePath.")] string? imageData = null,
        [Description(ToolDescriptions.Parameters.ImageFilePath)] string? filePath = null,
        [Description("Scale mode: FILL, FIT, CROP, TILE.")] string scaleMode = "FILL",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(imageData) == string.IsNullOrWhiteSpace(filePath))
            {
                return ToolResponse.Error("Pass exactly one of imageData or filePath.");
            }

            var bytes = filePath is { Length: > 0 } ? images.ReadAsBase64(filePath) : imageData!;

            var result = await Plugin.ApplyAsync(EditorCommands.SetImageFill, new JsonObject
            {
                ["nodeIds"] = new JsonArray(nodeId
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(id => (JsonNode)JsonValue.Create(NodeId.Normalize(id))!)
                    .ToArray()),
                ["imageData"] = bytes,
                ["scaleMode"] = scaleMode.ToUpperInvariant(),
            }, timeout: ImageTransferTimeout, cancellationToken: ct).ConfigureAwait(false);

            return BridgeRefusal(result) is { } refusal
                ? ToolResponse.Error(refusal, ImageRefusalHint)
                : ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_create_child", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Node.CreateChild)]
    public Task<string> CreateChildAsync(
        [Description("Id of the parent — a frame, a group or a page.")] string parentId,
        [Description("Type of the node to create.")] string nodeType,
        [Description("Properties of the new node as a JSON string.")] string? properties = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(properties, out var parsed, out var error))
            {
                return ToolResponse.Error($"properties did not parse as JSON: {error}",
                    "An object is expected, for example {\"name\":\"Button\",\"width\":120}.");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.CreateChildNode, new JsonObject
            {
                ["parentId"] = NodeId.Normalize(parentId),
                ["nodeType"] = nodeType.ToUpperInvariant(),
                ["properties"] = parsed,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_description", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Node.SetDescription)]
    public Task<string> SetDescriptionAsync(
        [Description("Id of the node — a component, a variant set or a style.")] string nodeId,
        [Description("Description text.")] string description,
        [Description("Markdown version of the description, when markup is needed.")] string? descriptionMarkdown = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.SetNodeDescription, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["description"] = description,
            ["descriptionMarkdown"] = descriptionMarkdown,
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_instance_properties", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Node.SetInstanceProperties)]
    public Task<string> SetInstancePropertiesAsync(
        [Description("Id of the INSTANCE node.")] string nodeId,
        [Description("Properties as a JSON string.")] string properties,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(properties, out var parsed, out var error) || parsed is not JsonObject values)
            {
                return ToolResponse.Error(
                    $"properties did not parse as a JSON object: {error ?? "the value is not an object"}",
                    "Expected {\"Label\":\"Buy\",\"Show Icon\":true}.");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.SetInstanceProperties, new JsonObject
            {
                ["nodeId"] = NodeId.Normalize(nodeId),
                ["properties"] = values,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_create_page", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Node.CreatePage)]
    public Task<string> CreatePageAsync(
        [Description("Name of the new page.")] string name,
        [Description("Switch the editor to the new page right away.")] bool makeCurrent = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                const page = figma.createPage();
                page.name = {{ScriptLiteral.Of(name)}};
                if ({{(makeCurrent ? "true" : "false")}}) { await figma.setCurrentPageAsync(page); }
                return { id: page.id, name: page.name };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 10000 },
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_create_node_from_svg", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Node.CreateNodeFromSvg)]
    public Task<string> CreateNodeFromSvgAsync(
        [Description("SVG markup, the full <svg>...</svg> element.")] string svg,
        [Description("Id of the parent frame or page; empty appends to the current page.")] string? parentId = null,
        [Description("Name of the created frame.")] string? name = null,
        [Description("X relative to the parent.")] double? x = null,
        [Description("Y relative to the parent.")] double? y = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(svg))
            {
                return ToolResponse.Error("svg is empty.", "Pass the full <svg>...</svg> markup.");
            }

            var script = $$"""
                const parentId = {{ScriptLiteral.Of(parentId is null ? null : NodeId.Normalize(parentId))}};
                let parent = figma.currentPage;
                if (parentId) {
                  parent = await figma.getNodeByIdAsync(parentId);
                  if (!parent) return { __error: 'Parent node ' + parentId + ' was not found.' };
                  if (typeof parent.appendChild !== 'function') return { __error: 'Node ' + parentId + ' cannot hold children.' };
                  if (parent.type === 'PAGE' && typeof parent.loadAsync === 'function') { await parent.loadAsync(); }
                }
                let frame;
                try {
                  frame = figma.createNodeFromSvg({{ScriptLiteral.Of(svg)}});
                } catch (e) {
                  return { __error: 'SVG did not parse: ' + ((e && e.message) ? e.message : String(e)) };
                }
                parent.appendChild(frame);
                const name = {{ScriptLiteral.Of(name)}};
                if (name) frame.name = name;
                const x = {{(x is { } vx ? vx.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}};
                const y = {{(y is { } vy ? vy.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}};
                if (x !== null) frame.x = x;
                if (y !== null) frame.y = y;
                return { id: frame.id, name: frame.name, width: frame.width, height: frame.height, parentId: parent.id };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 15000 },
                timeout: TimeSpan.FromSeconds(20),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error);
            }

            if (Plugin.Documents.Resolve()?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, ct).ConfigureAwait(false);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_place_image", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description(ToolDescriptions.Node.PlaceImage)]
    public Task<string> PlaceImageAsync(
        [Description("URL of the image; Figma downloads it itself, and only from hosts the plugin manifest allows.")] string? url = null,
        [Description("Raw image bytes as base64; it travels through the context, so use it for icons and filePath for photographs.")] string? base64 = null,
        [Description(ToolDescriptions.Parameters.ImageFilePath)] string? filePath = null,
        [Description("Id of the parent frame or page; empty appends to the current page.")] string? parentId = null,
        [Description("Name of the created rectangle.")] string? name = null,
        [Description("X relative to the parent.")] double? x = null,
        [Description("Y relative to the parent.")] double? y = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var sources = new[] { url, base64, filePath }.Count(value => !string.IsNullOrWhiteSpace(value));

            if (sources != 1)
            {
                return ToolResponse.Error("Pass exactly one of url, base64 or filePath.");
            }

            if (filePath is { Length: > 0 })
            {
                return await PlaceFromFileAsync(filePath, parentId, name, x, y, ct).ConfigureAwait(false);
            }

            var script = $$"""
                let image;
                try {
                  image = {{(url is { Length: > 0 }
                    ? $"await figma.createImageAsync({ScriptLiteral.Of(url)})"
                    : $"figma.createImage(figma.base64Decode({ScriptLiteral.Of(base64)}))")}};
                } catch (e) {
                  return { __error: 'Figma rejected the image: ' + ((e && e.message) ? e.message : String(e)) };
                }
                const size = await image.getSizeAsync();
                const parentId = {{ScriptLiteral.Of(parentId is null ? null : NodeId.Normalize(parentId))}};
                let parent = figma.currentPage;
                if (parentId) {
                  parent = await figma.getNodeByIdAsync(parentId);
                  if (!parent) return { __error: 'Parent node ' + parentId + ' was not found.' };
                  if (typeof parent.appendChild !== 'function') return { __error: 'Node ' + parentId + ' cannot hold children.' };
                  if (parent.type === 'PAGE' && typeof parent.loadAsync === 'function') { await parent.loadAsync(); }
                }
                const rect = figma.createRectangle();
                rect.resize(size.width, size.height);
                rect.fills = [{ type: 'IMAGE', imageHash: image.hash, scaleMode: 'FILL' }];
                parent.appendChild(rect);
                const name = {{ScriptLiteral.Of(name)}};
                if (name) rect.name = name;
                const x = {{(x is { } vx ? vx.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}};
                const y = {{(y is { } vy ? vy.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}};
                if (x !== null) rect.x = x;
                if (y !== null) rect.y = y;
                return { id: rect.id, width: size.width, height: size.height, imageHash: image.hash, parentId: parent.id };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 30000 },
                timeout: TimeSpan.FromSeconds(40),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error,
                    "Figma accepts PNG, JPG, GIF and WebP up to 4096x4096; the URL must be reachable from the desktop app.");
            }

            if (Plugin.Documents.Resolve()?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, ct).ConfigureAwait(false);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_create_node_from_jsx", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Node.CreateNodeFromJsx)]
    public Task<string> CreateNodeFromJsxAsync(
        [Description("""Node tree as JSON: {"type":"AutoLayout","props":{"fill":"#F00","padding":20},"children":[{"type":"Text","props":{"fontSize":24},"children":["Hello"]}]}. Components: AutoLayout, Frame, Text, Rectangle, Ellipse, Line, SVG, Image, Fragment.""")]
        string spec,
        [Description("Id of the parent frame or page; empty appends to the current page.")] string? parentId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(spec, out var parsed, out var parseError) || parsed is null)
            {
                return ToolResponse.Error($"spec did not parse as JSON: {parseError ?? "empty"}",
                    """Expected {"type":"AutoLayout","props":{...},"children":[...]}.""");
            }

            var script = $$"""
                const w = figma.widget;
                if (!w || typeof w.h !== 'function') {
                  return { __error: 'The JSX runtime (figma.widget.h) is not available in this Figma build.' };
                }
                const spec = {{parsed.ToJsonString()}};
                function build(s) {
                  if (typeof s === 'string' || typeof s === 'number') return String(s);
                  const component = w[s.type];
                  if (!component) throw new Error('Unknown JSX component: ' + s.type);
                  const children = (s.children || []).map(build);
                  return w.h.apply(null, [component, s.props || {}].concat(children));
                }
                let node;
                try {
                  node = await figma.createNodeFromJSXAsync(build(spec));
                } catch (e) {
                  return { __error: (e && e.message) ? e.message : String(e) };
                }
                const parentId = {{ScriptLiteral.Of(parentId is null ? null : NodeId.Normalize(parentId))}};
                if (parentId) {
                  const parent = await figma.getNodeByIdAsync(parentId);
                  if (!parent) return { __error: 'Created ' + node.id + ', but parent ' + parentId + ' was not found.' };
                  if (parent.type === 'PAGE' && typeof parent.loadAsync === 'function') { await parent.loadAsync(); }
                  parent.appendChild(node);
                }
                return { id: node.id, name: node.name, type: node.type, width: node.width, height: node.height };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 30000 },
                timeout: TimeSpan.FromSeconds(40),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error,
                    "JSX supports a subset of node features — style ids and instances cannot be rendered; " +
                    "create the node and set the rest through figma_set_fills or figma_execute.");
            }

            if (Plugin.Documents.Resolve()?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, ct).ConfigureAwait(false);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_boolean_operation", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Node.BooleanOperation)]
    public Task<string> BooleanOperationAsync(
        [Description("Operation: UNION, SUBTRACT, INTERSECT, EXCLUDE or FLATTEN.")] string operation,
        [Description("Comma-separated node ids; the order matters for SUBTRACT.")] string nodeIds,
        [Description("Name of the resulting node.")] string? name = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var op = operation.Trim().ToUpperInvariant();

            if (op is not ("UNION" or "SUBTRACT" or "INTERSECT" or "EXCLUDE" or "FLATTEN"))
            {
                return ToolResponse.Error($"Unknown operation '{operation}'.",
                    "Expected UNION, SUBTRACT, INTERSECT, EXCLUDE or FLATTEN.");
            }

            var ids = nodeIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NodeId.Normalize)
                .ToArray();

            if (ids.Length < (op == "FLATTEN" ? 1 : 2))
            {
                return ToolResponse.Error(
                    $"{op} needs at least {(op == "FLATTEN" ? 1 : 2)} node ids, got {ids.Length}.");
            }

            var script = $$"""
                const ids = {{ScriptLiteral.Of(ids)}};
                const nodes = [];
                for (const id of ids) {
                  const node = await figma.getNodeByIdAsync(id);
                  if (!node) return { __error: 'Node ' + id + ' was not found.' };
                  nodes.push(node);
                }
                const parent = nodes[0].parent || figma.currentPage;
                const op = {{ScriptLiteral.Of(op)}};
                let result;
                try {
                  if (op === 'UNION') result = figma.union(nodes, parent);
                  else if (op === 'SUBTRACT') result = figma.subtract(nodes, parent);
                  else if (op === 'INTERSECT') result = figma.intersect(nodes, parent);
                  else if (op === 'EXCLUDE') result = figma.exclude(nodes, parent);
                  else result = figma.flatten(nodes, parent);
                } catch (e) {
                  return { __error: (e && e.message) ? e.message : String(e) };
                }
                const name = {{ScriptLiteral.Of(name)}};
                if (name) result.name = name;
                return { id: result.id, type: result.type, name: result.name, parentId: parent.id };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 20000 },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error, "Boolean operations need vector-compatible nodes on the same page.");
            }

            if (Plugin.Documents.Resolve()?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, ct).ConfigureAwait(false);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_group_nodes", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Node.GroupNodes)]
    public Task<string> GroupNodesAsync(
        [Description("Comma-separated ids of the nodes to group.")] string nodeIds,
        [Description("Name of the group.")] string? name = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var ids = nodeIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NodeId.Normalize)
                .ToArray();

            if (ids.Length == 0)
            {
                return ToolResponse.Error("nodeIds is empty.");
            }

            var script = $$"""
                const ids = {{ScriptLiteral.Of(ids)}};
                const nodes = [];
                for (const id of ids) {
                  const node = await figma.getNodeByIdAsync(id);
                  if (!node) return { __error: 'Node ' + id + ' was not found.' };
                  nodes.push(node);
                }
                let group;
                try {
                  group = figma.group(nodes, nodes[0].parent || figma.currentPage);
                } catch (e) {
                  return { __error: (e && e.message) ? e.message : String(e) };
                }
                const name = {{ScriptLiteral.Of(name)}};
                if (name) group.name = name;
                return { id: group.id, name: group.name, children: group.children.length };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 15000 },
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error, "The nodes must share a page; locked or removed nodes cannot be grouped.");
            }

            if (Plugin.Documents.Resolve()?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, ct).ConfigureAwait(false);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_ungroup_node", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Node.UngroupNode)]
    public Task<string> UngroupNodeAsync(
        [Description("Id of the group or frame to dissolve.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                const node = await figma.getNodeByIdAsync({{ScriptLiteral.Of(NodeId.Normalize(nodeId))}});
                if (!node) return { __error: 'Node was not found.' };
                let children;
                try {
                  children = figma.ungroup(node);
                } catch (e) {
                  return { __error: (e && e.message) ? e.message : String(e) };
                }
                return { released: children.map(function (c) { return { id: c.id, name: c.name }; }) };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 15000 },
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error, Messages.CheckNodeIdHint);
            }

            if (Plugin.Documents.Resolve()?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, ct).ConfigureAwait(false);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    /// <summary>Places a picture read from disk in three hops: the rectangle first, then the bytes as a command parameter, then the natural size.</summary>
    /// <remarks>The bytes cannot ride inside the script the way <c>base64</c> does: an inlined photograph is handed to the sandbox parser as source text, and the rectangle is created first only so the existing image-fill command has a node to fill. A hop that fails takes the empty rectangle with it.</remarks>
    private async Task<string> PlaceFromFileAsync(
        string filePath,
        string? parentId,
        string? name,
        double? x,
        double? y,
        CancellationToken cancellationToken)
    {
        var data = images.ReadAsBase64(filePath);

        var createScript = $$"""
            const parentId = {{ScriptLiteral.Of(parentId is null ? null : NodeId.Normalize(parentId))}};
            let parent = figma.currentPage;
            if (parentId) {
              parent = await figma.getNodeByIdAsync(parentId);
              if (!parent) return { __error: 'Parent node ' + parentId + ' was not found.' };
              if (typeof parent.appendChild !== 'function') return { __error: 'Node ' + parentId + ' cannot hold children.' };
              if (parent.type === 'PAGE' && typeof parent.loadAsync === 'function') { await parent.loadAsync(); }
            }
            const rect = figma.createRectangle();
            parent.appendChild(rect);
            const name = {{ScriptLiteral.Of(name)}};
            if (name) rect.name = name;
            const x = {{Coordinate(x)}};
            const y = {{Coordinate(y)}};
            if (x !== null) rect.x = x;
            if (y !== null) rect.y = y;
            return { id: rect.id, parentId: parent.id };
            """;

        var created = Unwrap(await Plugin.ApplyAsync(
            EditorCommands.ExecuteCode,
            new JsonObject { ["code"] = createScript, ["timeout"] = 15000 },
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken: cancellationToken).ConfigureAwait(false));

        if (created?["__error"]?.GetValue<string>() is { } createError)
        {
            return ToolResponse.Error(createError);
        }

        if (created?["id"]?.GetValue<string>() is not { } nodeId)
        {
            return ToolResponse.Error("The plugin created no rectangle to put the picture in.");
        }

        try
        {
            var filled = await Plugin.ApplyAsync(EditorCommands.SetImageFill, new JsonObject
            {
                ["nodeIds"] = new JsonArray(JsonValue.Create(nodeId)),
                ["imageData"] = data,
                ["scaleMode"] = "FILL",
            }, timeout: ImageTransferTimeout, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (BridgeRefusal(filled) is { } refusal)
            {
                throw new InvalidOperationException(refusal);
            }

            if (filled?["imageHash"]?.GetValue<string>() is not { } hash)
            {
                throw new InvalidOperationException($"The plugin took the picture but named no image for node {nodeId}.");
            }

            var sizeScript = $$"""
                const node = await figma.getNodeByIdAsync({{ScriptLiteral.Of(nodeId)}});
                if (!node) return { __error: 'The rectangle was gone before the picture could be sized.' };
                const image = figma.getImageByHash({{ScriptLiteral.Of(hash)}});
                if (!image) return { __error: 'Figma no longer knows the image that was just created.' };
                const size = await image.getSizeAsync();
                node.resize(size.width, size.height);
                return { id: node.id, name: node.name, width: size.width, height: size.height, imageHash: {{ScriptLiteral.Of(hash)}}, parentId: node.parent ? node.parent.id : null };
                """;

            var sized = Unwrap(await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = sizeScript, ["timeout"] = 15000 },
                timeout: TimeSpan.FromSeconds(20),
                cancellationToken: cancellationToken).ConfigureAwait(false));

            if (sized?["__error"]?.GetValue<string>() is { } sizeError)
            {
                throw new InvalidOperationException(sizeError);
            }

            if (Plugin.Documents.Resolve()?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, cancellationToken).ConfigureAwait(false);
            }

            return ToolResponse.Ok(sized ?? new JsonObject { ["id"] = nodeId });
        }
        catch (Exception ex)
        {
            await DiscardAsync(nodeId).ConfigureAwait(false);

            if (ex is InvalidOperationException or EditorCommandException)
            {
                return ToolResponse.Error(ex.Message, ImageRefusalHint);
            }

            throw;
        }
    }

    /// <summary>The plugin's own words when it could not build the picture, or <c>null</c> when it did.</summary>
    /// <remarks>It reports this inside the reply instead of failing the command, so a bare <c>ok</c> would hide the real cause — "in createImage: Image is too large" was observed exactly this way.</remarks>
    private static string? BridgeRefusal(JsonNode? reply) =>
        reply?["success"]?.GetValue<bool>() == false
            ? reply["error"]?.GetValue<string>() ?? "The plugin refused the picture without saying why."
            : null;

    /// <summary>Removes the rectangle a failed placement would otherwise leave on the canvas.</summary>
    /// <remarks>It takes no cancellation token on purpose: a cancelled placement is exactly when the canvas would keep an empty rectangle. The caller is already failing, so a failure here is only logged.</remarks>
    private async Task DiscardAsync(string nodeId)
    {
        try
        {
            await Plugin.ApplyAsync(
                EditorCommands.DeleteNode,
                new JsonObject { ["nodeId"] = nodeId },
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is EditorUnavailableException or EditorCommandException or TimeoutException or InvalidOperationException)
        {
            Log.RectangleLeftBehind(Logger, nodeId, ex.Message);
        }
    }

    /// <summary>Writes an optional coordinate as a JavaScript literal.</summary>
    /// <remarks>The invariant culture keeps a comma locale from producing <c>1,5</c>, which the sandbox would read as two arguments.</remarks>
    private static string Coordinate(double? value) =>
        value is { } number ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
}
