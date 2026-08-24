using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Node operations: clone, delete, rename, strokes, images, child creation.</summary>
[McpServerToolType]
public sealed class NodeTools(ILiveEditor plugin, ILogger<NodeTools> logger)
    : EditorToolBase(plugin, logger)
{
    [McpServerTool(Name = "figma_clone_node")]
    [Description(ToolDescriptions.Node.CloneNode)]
    public Task<string> CloneNodeAsync(
        [Description("Node id, for example '1:234'.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CloneNode,
            new JsonObject { ["nodeId"] = NodeId.Normalize(nodeId) }, cancellationToken);

    [McpServerTool(Name = "figma_delete_node")]
    [Description(ToolDescriptions.Node.DeleteNode)]
    public Task<string> DeleteNodeAsync(
        [Description("Node id.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.DeleteNode,
            new JsonObject { ["nodeId"] = NodeId.Normalize(nodeId) }, cancellationToken);

    [McpServerTool(Name = "figma_rename_node")]
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

    [McpServerTool(Name = "figma_set_strokes")]
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

    [McpServerTool(Name = "figma_set_image_fill")]
    [Description(ToolDescriptions.Node.SetImageFill)]
    public Task<string> SetImageFillAsync(
        [Description("Node id, or several separated by commas.")] string nodeId,
        [Description("Image as base64.")] string imageData,
        [Description("Scale mode: FILL, FIT, CROP, TILE.")] string scaleMode = "FILL",
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.SetImageFill, new JsonObject
        {
            ["nodeIds"] = new JsonArray(nodeId
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => (JsonNode)JsonValue.Create(NodeId.Normalize(id))!)
                .ToArray()),
            ["imageData"] = imageData,
            ["scaleMode"] = scaleMode.ToUpperInvariant(),
        }, cancellationToken, TimeSpan.FromSeconds(60));

    [McpServerTool(Name = "figma_create_child")]
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

    [McpServerTool(Name = "figma_set_description")]
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

    [McpServerTool(Name = "figma_set_instance_properties")]
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

    [McpServerTool(Name = "figma_create_page")]
    [Description(ToolDescriptions.Node.CreatePage)]
    public Task<string> CreatePageAsync(
        [Description("Name of the new page.")] string name,
        [Description("Switch the editor to the new page right away.")] bool makeCurrent = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                const page = figma.createPage();
                page.name = {{JsonSerializer.Serialize(name)}};
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

    [McpServerTool(Name = "figma_create_node_from_svg")]
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
                const parentId = {{JsonSerializer.Serialize(parentId is null ? null : NodeId.Normalize(parentId))}};
                let parent = figma.currentPage;
                if (parentId) {
                  parent = await figma.getNodeByIdAsync(parentId);
                  if (!parent) return { __error: 'Parent node ' + parentId + ' was not found.' };
                  if (typeof parent.appendChild !== 'function') return { __error: 'Node ' + parentId + ' cannot hold children.' };
                  if (parent.type === 'PAGE' && typeof parent.loadAsync === 'function') { await parent.loadAsync(); }
                }
                let frame;
                try {
                  frame = figma.createNodeFromSvg({{JsonSerializer.Serialize(svg)}});
                } catch (e) {
                  return { __error: 'SVG did not parse: ' + ((e && e.message) ? e.message : String(e)) };
                }
                parent.appendChild(frame);
                const name = {{JsonSerializer.Serialize(name)}};
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

    [McpServerTool(Name = "figma_place_image")]
    [Description(ToolDescriptions.Node.PlaceImage)]
    public Task<string> PlaceImageAsync(
        [Description("URL of the image; Figma downloads it itself. Pass either url or base64.")] string? url = null,
        [Description("Raw image bytes as base64 — for local files.")] string? base64 = null,
        [Description("Id of the parent frame or page; empty appends to the current page.")] string? parentId = null,
        [Description("Name of the created rectangle.")] string? name = null,
        [Description("X relative to the parent.")] double? x = null,
        [Description("Y relative to the parent.")] double? y = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(url) == string.IsNullOrWhiteSpace(base64))
            {
                return ToolResponse.Error("Pass exactly one of url or base64.");
            }

            var script = $$"""
                let image;
                try {
                  image = {{(url is not null
                    ? $"await figma.createImageAsync({JsonSerializer.Serialize(url)})"
                    : $"figma.createImage(figma.base64Decode({JsonSerializer.Serialize(base64)}))")}};
                } catch (e) {
                  return { __error: 'Figma rejected the image: ' + ((e && e.message) ? e.message : String(e)) };
                }
                const size = await image.getSizeAsync();
                const parentId = {{JsonSerializer.Serialize(parentId is null ? null : NodeId.Normalize(parentId))}};
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
                const name = {{JsonSerializer.Serialize(name)}};
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

    [McpServerTool(Name = "figma_create_node_from_jsx")]
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
                const parentId = {{JsonSerializer.Serialize(parentId is null ? null : NodeId.Normalize(parentId))}};
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

    [McpServerTool(Name = "figma_boolean_operation")]
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
                const ids = {{JsonSerializer.Serialize(ids)}};
                const nodes = [];
                for (const id of ids) {
                  const node = await figma.getNodeByIdAsync(id);
                  if (!node) return { __error: 'Node ' + id + ' was not found.' };
                  nodes.push(node);
                }
                const parent = nodes[0].parent || figma.currentPage;
                const op = {{JsonSerializer.Serialize(op)}};
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
                const name = {{JsonSerializer.Serialize(name)}};
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

    [McpServerTool(Name = "figma_group_nodes")]
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
                const ids = {{JsonSerializer.Serialize(ids)}};
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
                const name = {{JsonSerializer.Serialize(name)}};
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

    [McpServerTool(Name = "figma_ungroup_node")]
    [Description(ToolDescriptions.Node.UngroupNode)]
    public Task<string> UngroupNodeAsync(
        [Description("Id of the group or frame to dissolve.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                const node = await figma.getNodeByIdAsync({{JsonSerializer.Serialize(NodeId.Normalize(nodeId))}});
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
}
