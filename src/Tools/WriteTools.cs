using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>WRITE channel: mutations go only through the plugin, since the REST API cannot write nodes.</summary>
[McpServerToolType]
public sealed class WriteTools(ILiveEditor plugin, ILogger<WriteTools> logger)
    : EditorToolBase(plugin, logger)
{
    [McpServerTool(Name = "figma_get_selection")]
    [Description(ToolDescriptions.Write.GetSelection)]
    public Task<string> GetSelectionAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ =>
        {
            var session = Plugin.Documents.Resolve();

            if (session is null)
            {
                return Task.FromResult(NotConnected());
            }

            return Task.FromResult(ToolResponse.Ok(
                session.Selection?.DeepClone() ?? new JsonObject { ["nodes"] = new JsonArray(), ["count"] = 0 },
                envelope =>
                {
                    envelope["fileKey"] = session.FileKey;
                    envelope["fileName"] = session.FileName;
                    envelope["currentPage"] = session.CurrentPage;
                }));
        }, cancellationToken);

    [McpServerTool(Name = "figma_move_node")]
    [Description(ToolDescriptions.Write.MoveNode)]
    public Task<string> MoveNodeAsync(
        [Description("Node id, for example '1:234'.")] string nodeId,
        [Description("New X coordinate relative to the parent.")] double x,
        [Description("New Y coordinate relative to the parent.")] double y,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.MoveNode, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["x"] = x,
            ["y"] = y,
        }, cancellationToken);

    [McpServerTool(Name = "figma_resize_node")]
    [Description(ToolDescriptions.Write.ResizeNode)]
    public Task<string> ResizeNodeAsync(
        [Description("Node id, for example '1:234'.")] string nodeId,
        [Description("New width in pixels.")] double width,
        [Description("New height in pixels.")] double height,
        [Description("Recalculate the children by their constraints.")] bool withConstraints = true,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.ResizeNode, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["width"] = width,
            ["height"] = height,
            ["withConstraints"] = withConstraints,
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_fills")]
    [Description(ToolDescriptions.Write.SetFills)]
    public Task<string> SetFillsAsync(
        [Description("Node id, for example '1:234'.")] string nodeId,
        [Description("Array of Paint as a JSON string.")] string fills,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(fills, out var parsed, out var error) || parsed is null)
            {
                return ToolResponse.Error($"fills did not parse as JSON: {error}",
                    "An array is expected, for example [{\"type\":\"SOLID\",\"color\":{\"r\":1,\"g\":0,\"b\":0}}].");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.SetNodeFills, new JsonObject
            {
                ["nodeId"] = NodeId.Normalize(nodeId),
                ["fills"] = parsed,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject());
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_text")]
    [Description(ToolDescriptions.Write.SetText)]
    public Task<string> SetTextAsync(
        [Description("TEXT node id, for example '1:234'.")] string nodeId,
        [Description("New text.")] string text,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.SetTextContent, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["text"] = text,
        }, cancellationToken);

    [McpServerTool(Name = "figma_execute")]
    [Description(ToolDescriptions.Write.Execute)]
    public Task<string> ExecuteAsync(
        [Description("JavaScript code. Example: const n = await figma.getNodeByIdAsync('1:2'); return n.name;")]
        string code,
        [Description("Execution timeout in milliseconds.")] int timeoutMs = 15000,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var result = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = code, ["timeout"] = timeoutMs },
                timeout: TimeSpan.FromMilliseconds(timeoutMs + 5000),
                cancellationToken: ct).ConfigureAwait(false);

            var session = Plugin.Documents.Resolve();

            if (session?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, ct).ConfigureAwait(false);
            }

            return ToolResponse.Ok(result ?? new JsonObject());
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_selection_colors")]
    [Description(ToolDescriptions.Write.GetSelectionColors)]
    public Task<string> GetSelectionColorsAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            const string script = """
                const colors = figma.getSelectionColors();
                if (!colors) return { empty: true, note: 'Nothing is selected, or the selection holds too many colors.' };
                return {
                  paints: colors.paints,
                  styles: colors.styles.map(function (s) { return { id: s.id, name: s.name }; }),
                };
                """;

            var raw = await Plugin.ReadAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 10000 },
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            return payload?["__error"]?.GetValue<string>() is { } error
                ? ToolResponse.Error(error)
                : ToolResponse.Ok(payload ?? new JsonObject());
        }, cancellationToken);

    [McpServerTool(Name = "figma_sort_styles")]
    [Description(ToolDescriptions.Write.SortStyles)]
    public Task<string> SortStylesAsync(
        [Description("Style type: PAINT, TEXT, EFFECT or GRID.")] string styleType,
        [Description("Comma-separated style ids in the desired order; empty sorts alphabetically.")] string? order = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var type = styleType.Trim().ToUpperInvariant();

            if (type is not ("PAINT" or "TEXT" or "EFFECT" or "GRID"))
            {
                return ToolResponse.Error($"Unknown styleType '{styleType}'.", "Expected PAINT, TEXT, EFFECT or GRID.");
            }

            var explicitOrder = order?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

            var script = $$"""
                const type = {{JsonSerializer.Serialize(type)}};
                const getters = {
                  PAINT: function () { return figma.getLocalPaintStylesAsync(); },
                  TEXT: function () { return figma.getLocalTextStylesAsync(); },
                  EFFECT: function () { return figma.getLocalEffectStylesAsync(); },
                  GRID: function () { return figma.getLocalGridStylesAsync(); },
                };
                const movers = {
                  PAINT: function (s, after) { figma.moveLocalPaintStyleAfter(s, after); },
                  TEXT: function (s, after) { figma.moveLocalTextStyleAfter(s, after); },
                  EFFECT: function (s, after) { figma.moveLocalEffectStyleAfter(s, after); },
                  GRID: function (s, after) { figma.moveLocalGridStyleAfter(s, after); },
                };
                const styles = await getters[type]();
                if (styles.length === 0) return { sorted: 0, note: 'The file has no local ' + type + ' styles.' };
                const wanted = {{JsonSerializer.Serialize(explicitOrder)}};
                let target;
                if (wanted.length > 0) {
                  const byId = {};
                  for (const s of styles) byId[s.id] = s;
                  target = [];
                  for (const id of wanted) {
                    if (!byId[id]) return { __error: 'Style ' + id + ' was not found among local ' + type + ' styles.' };
                    target.push(byId[id]);
                  }
                } else {
                  target = styles.slice().sort(function (a, b) { return a.name.localeCompare(b.name); });
                }
                let previous = null;
                try {
                  for (const style of target) {
                    movers[type](style, previous);
                    previous = style;
                  }
                } catch (e) {
                  return { __error: (e && e.message) ? e.message : String(e) };
                }
                return { sorted: target.length, first: target[0].name, last: target[target.length - 1].name };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 20000 },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error, "Style ids come from figma_get_styles (the id field).");
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_commit_undo")]
    [Description(ToolDescriptions.Write.CommitUndo)]
    public Task<string> CommitUndoAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = "figma.commitUndo(); return { committed: true };", ["timeout"] = 5000 },
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["committed"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_create_style")]
    [Description(ToolDescriptions.Write.CreateStyle)]
    public Task<string> CreateStyleAsync(
        [Description("Style type: PAINT, TEXT, EFFECT or GRID.")] string styleType,
        [Description("Style name; a slash makes a folder: Brand/Primary.")] string name,
        [Description("Style contents as JSON, keyed by type: paints, text fields, effects or layoutGrids.")]
        string? properties = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var type = styleType.Trim().ToUpperInvariant();

            if (type is not ("PAINT" or "TEXT" or "EFFECT" or "GRID"))
            {
                return ToolResponse.Error(
                    $"Unknown styleType '{styleType}'.",
                    "Expected PAINT, TEXT, EFFECT or GRID.");
            }

            if (!TryParseJson(properties, out var parsed, out var parseError))
            {
                return ToolResponse.Error($"properties did not parse as JSON: {parseError}",
                    """For PAINT: {"paints":[{"type":"SOLID","color":{"r":1,"g":0,"b":0}}]}.""");
            }

            var script = $$"""
                const type = {{JsonSerializer.Serialize(type)}};
                const props = {{parsed?.ToJsonString() ?? "{}"}};
                let style;
                if (type === 'PAINT') style = figma.createPaintStyle();
                else if (type === 'TEXT') style = figma.createTextStyle();
                else if (type === 'EFFECT') style = figma.createEffectStyle();
                else style = figma.createGridStyle();
                style.name = {{JsonSerializer.Serialize(name)}};
                try {
                  if (type === 'PAINT' && props.paints) style.paints = props.paints;
                  if (type === 'TEXT') {
                    if (props.fontName) { await figma.loadFontAsync(props.fontName); style.fontName = props.fontName; }
                    for (const f of ['fontSize', 'lineHeight', 'letterSpacing', 'paragraphSpacing', 'textCase', 'textDecoration']) {
                      if (props[f] !== undefined) style[f] = props[f];
                    }
                  }
                  if (type === 'EFFECT' && props.effects) style.effects = props.effects;
                  if (type === 'GRID' && props.layoutGrids) style.layoutGrids = props.layoutGrids;
                } catch (e) {
                  style.remove();
                  return { __error: (e && e.message) ? e.message : String(e) };
                }
                return { id: style.id, name: style.name, styleType: type };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 15000 },
                timeout: TimeSpan.FromSeconds(20),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error,
                    """Check the properties shape: paints/effects/layoutGrids are arrays, fontName is {"family":"...","style":"..."}.""");
            }

            if (Plugin.Documents.Resolve()?.FileKey is { } fileKey)
            {
                await Plugin.InvalidateWholeFileAsync(fileKey, ct).ConfigureAwait(false);
            }

            return ToolResponse.Ok(payload ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_status")]
    [Description(ToolDescriptions.Write.GetStatus)]
    public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ =>
        {
            var sessions = new JsonArray(Plugin.Documents.Sessions.Select(s => (JsonNode)s.Describe()).ToArray());

            return Task.FromResult(ToolResponse.Ok(new JsonObject
            {
                ["connected"] = Plugin.IsConnected,
                ["activeFileKey"] = Plugin.Documents.ActiveFileKey,
                ["targetLocked"] = Plugin.Documents.IsLocked,
                ["files"] = sessions,
            }));
        }, cancellationToken);
}
