using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>FigJam board: stickies, labelled shapes, connectors, sections, tables and code blocks; the plugin rejects them outside FigJam files.</summary>
[McpServerToolType]
public sealed class FigJamTools(ILiveEditor plugin, ILogger<FigJamTools> logger)
    : EditorToolBase(plugin, logger)
{
    private const int MaxBatchSize = 200;
    private const int MaxArrangeNodes = 500;

    [McpServerTool(Name = "figjam_create_sticky", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.CreateSticky)]
    public Task<string> CreateStickyAsync(
        [Description("Sticky text.")] string text,
        [Description("Sticky color.")] string? color = null,
        [Description("X coordinate on the canvas.")] double? x = null,
        [Description("Y coordinate on the canvas.")] double? y = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CreateSticky, new JsonObject
        {
            ["text"] = text,
            ["color"] = color,
            ["x"] = x,
            ["y"] = y,
        }, cancellationToken);

    [McpServerTool(Name = "figjam_create_stickies", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.CreateStickies)]
    public Task<string> CreateStickiesAsync(
        [Description("Array of stickies as a JSON string.")] string stickies,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(stickies, out var parsed, out var error) || parsed is not JsonArray array)
            {
                return ToolResponse.Error(
                    $"stickies did not parse as a JSON array: {error ?? "the value is not an array"}",
                    "Expected [{\"text\":\"Idea\",\"color\":\"BLUE\"}].");
            }

            if (array.Count > MaxBatchSize)
            {
                return ToolResponse.Error(
                    $"There are {array.Count} stickies, and at most {MaxBatchSize} fit in one call.",
                    "Split it into several calls — otherwise the plugin will not make it in time.");
            }

            var result = await Plugin.ApplyAsync(
                EditorCommands.CreateStickies,
                new JsonObject { ["stickies"] = array.DeepClone() },
                timeout: TimeSpan.FromSeconds(45),
                cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figjam_create_connector", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.CreateConnector)]
    public Task<string> CreateConnectorAsync(
        [Description("Id of the start node.")] string startNodeId,
        [Description("Id of the end node.")] string endNodeId,
        [Description("Label on the connector.")] string? label = null,
        [Description("Magnet on the start node.")] string startMagnet = "AUTO",
        [Description("Magnet on the end node.")] string endMagnet = "AUTO",
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CreateConnector, new JsonObject
        {
            ["startNodeId"] = NodeId.Normalize(startNodeId),
            ["endNodeId"] = NodeId.Normalize(endNodeId),
            ["label"] = label,
            ["startMagnet"] = startMagnet,
            ["endMagnet"] = endMagnet,
        }, cancellationToken);

    [McpServerTool(Name = "figjam_create_shape_with_text", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.CreateShapeWithText)]
    public Task<string> CreateShapeWithTextAsync(
        [Description("Label on the shape.")] string? text = null,
        [Description("Shape type.")] string? shapeType = null,
        [Description("X coordinate.")] double? x = null,
        [Description("Y coordinate.")] double? y = null,
        [Description("Width in pixels.")] double? width = null,
        [Description("Height in pixels.")] double? height = null,
        [Description("Fill color in hex, for example '#E1F5EE'.")] string? fillColor = null,
        [Description("Stroke color in hex.")] string? strokeColor = null,
        [Description("Label font size in pixels.")] double? fontSize = null,
        [Description("Stroke dash pattern as comma-separated numbers, for example '10,5'.")] string? strokeDashPattern = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CreateShapeWithText, new JsonObject
        {
            ["text"] = text,
            ["shapeType"] = shapeType,
            ["x"] = x,
            ["y"] = y,
            ["width"] = width,
            ["height"] = height,
            ["fillColor"] = fillColor,
            ["strokeColor"] = strokeColor,
            ["fontSize"] = fontSize,
            ["strokeDashPattern"] = strokeDashPattern,
        }, cancellationToken);

    [McpServerTool(Name = "figjam_create_section", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.CreateSection)]
    public Task<string> CreateSectionAsync(
        [Description("Section name.")] string? name = null,
        [Description("X coordinate.")] double? x = null,
        [Description("Y coordinate.")] double? y = null,
        [Description("Width in pixels.")] double width = 1000,
        [Description("Height in pixels.")] double height = 800,
        [Description("Fill color in hex.")] string? fillColor = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CreateSection, new JsonObject
        {
            ["name"] = name,
            ["x"] = x,
            ["y"] = y,
            ["width"] = width,
            ["height"] = height,
            ["fillColor"] = fillColor,
        }, cancellationToken);

    [McpServerTool(Name = "figjam_create_table", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.CreateTable)]
    public Task<string> CreateTableAsync(
        [Description("Number of rows.")] int rows,
        [Description("Number of columns.")] int columns,
        [Description("Cell contents as a JSON string.")] string? data = null,
        [Description("X coordinate.")] double? x = null,
        [Description("Y coordinate.")] double? y = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(data, out var parsed, out var error))
            {
                return ToolResponse.Error(
                    $"data did not parse as JSON: {error}",
                    "An array of arrays of strings is expected: [[\"What\",\"Who\"],[\"Design\",\"Ann\"]].");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.CreateTable, new JsonObject
            {
                ["rows"] = rows,
                ["columns"] = columns,
                ["data"] = parsed?.DeepClone(),
                ["x"] = x,
                ["y"] = y,
            }, timeout: TimeSpan.FromSeconds(45), cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figjam_create_code_block", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.CreateCodeBlock)]
    public Task<string> CreateCodeBlockAsync(
        [Description("Block contents.")] string code,
        [Description("Highlighting language.")] string? language = null,
        [Description("X coordinate.")] double? x = null,
        [Description("Y coordinate.")] double? y = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CreateCodeBlock, new JsonObject
        {
            ["code"] = code,
            ["language"] = language,
            ["x"] = x,
            ["y"] = y,
        }, cancellationToken);

    [McpServerTool(Name = "figjam_auto_arrange", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.AutoArrange)]
    public Task<string> AutoArrangeAsync(
        [Description("Comma-separated node ids.")] string nodeIds,
        [Description("Layout type: grid, horizontal, vertical.")] string layout = "grid",
        [Description("Gap between nodes in pixels.")] double spacing = 40,
        [Description("Number of columns for the grid. Defaults to the square root of the node count.")] int? columns = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var ids = nodeIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NodeId.Normalize)
                .ToList();

            if (ids.Count == 0)
            {
                return ToolResponse.Error("No node id was passed.");
            }

            if (ids.Count > MaxArrangeNodes)
            {
                return ToolResponse.Error($"There are {ids.Count} nodes, and at most {MaxArrangeNodes} fit in one call.");
            }

            var gridColumns = columns ?? (int)Math.Ceiling(Math.Sqrt(ids.Count));
            var parameters = new JsonObject
            {
                ["nodeIds"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
                ["layout"] = layout,
                ["spacing"] = spacing,
                ["gridCols"] = Math.Max(1, gridColumns),
            };

            var code = $$"""
                const params = JSON.parse({{ToJsString(parameters.ToJsonString())}});
                const nodes = [];
                for (const id of params.nodeIds) {
                  const node = await figma.getNodeByIdAsync(id);
                  if (node) nodes.push(node);
                }
                if (nodes.length === 0) throw new Error('No valid nodes found');

                let x = nodes[0].x;
                let y = nodes[0].y;
                const startX = x;
                let maxRowHeight = 0;

                for (let i = 0; i < nodes.length; i++) {
                  const node = nodes[i];
                  if (params.layout === 'horizontal') {
                    node.x = x;
                    node.y = y;
                    x += node.width + params.spacing;
                  } else if (params.layout === 'vertical') {
                    node.x = x;
                    node.y = y;
                    y += node.height + params.spacing;
                  } else {
                    const col = i % params.gridCols;
                    if (col === 0 && i > 0) {
                      y += maxRowHeight + params.spacing;
                      maxRowHeight = 0;
                      x = startX;
                    }
                    node.x = x;
                    node.y = y;
                    maxRowHeight = Math.max(maxRowHeight, node.height);
                    x += node.width + params.spacing;
                  }
                }
                return { arranged: nodes.length, layout: params.layout };
                """;

            var result = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject
                {
                    ["code"] = code,
                    ["timeout"] = 10000,
                    ["nodeIds"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
                },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figjam_get_board_contents", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.GetBoardContents)]
    public Task<string> GetBoardContentsAsync(
        [Description("Comma-separated node types. Empty means all of them.")] string? nodeTypes = null,
        [Description("Cap on the number of nodes in the response.")] int maxNodes = 500,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetBoardContents, new JsonObject
        {
            ["nodeTypes"] = SplitToArray(nodeTypes),
            ["maxNodes"] = Math.Clamp(maxNodes, 1, 1000),
        }, cancellationToken, TimeSpan.FromSeconds(45));

    [McpServerTool(Name = "figjam_get_connections", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.FigJam.GetConnections)]
    public Task<string> GetConnectionsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetConnections, [], cancellationToken, TimeSpan.FromSeconds(30));

    /// <summary>String into a JavaScript literal via JSON serialization.</summary>
    private static string ToJsString(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static JsonArray? SplitToArray(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : [.. value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => (JsonNode)JsonValue.Create(item)!)];
}
