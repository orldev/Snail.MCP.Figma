using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Figma Slides: slide grid, content, transitions and view mode — plugin only, REST does not serve this file type.</summary>
[McpServerToolType]
public sealed class SlidesTools(ILiveEditor plugin, ILogger<SlidesTools> logger)
    : EditorToolBase(plugin, logger)
{
    private const int MaxGridRows = 50;
    private const int MaxGridRow = 50;

    [McpServerTool(Name = "figma_list_slides")]
    [Description(ToolDescriptions.Slides.ListSlides)]
    public Task<string> ListSlidesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.ListSlides, new JsonObject(), cancellationToken);

    [McpServerTool(Name = "figma_get_slide_content")]
    [Description(ToolDescriptions.Slides.GetSlideContent)]
    public Task<string> GetSlideContentAsync(
        [Description("Slide id, for example '1:23'.")] string slideId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetSlideContent, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_slide_grid")]
    [Description(ToolDescriptions.Slides.GetSlideGrid)]
    public Task<string> GetSlideGridAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetSlideGrid, new JsonObject(), cancellationToken);

    [McpServerTool(Name = "figma_get_slide_transition")]
    [Description(ToolDescriptions.Slides.GetSlideTransition)]
    public Task<string> GetSlideTransitionAsync(
        [Description("Slide id.")] string slideId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetSlideTransition, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_focused_slide")]
    [Description(ToolDescriptions.Slides.GetFocusedSlide)]
    public Task<string> GetFocusedSlideAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetFocusedSlide, new JsonObject(), cancellationToken);

    [McpServerTool(Name = "figma_create_slide")]
    [Description(ToolDescriptions.Slides.CreateSlide)]
    public Task<string> CreateSlideAsync(
        [Description("Row index in the grid, zero-based.")] int? row = null,
        [Description("Column index in the grid, zero-based.")] int? col = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CreateSlide, new JsonObject
        {
            ["row"] = row,
            ["col"] = col,
        }, cancellationToken);

    [McpServerTool(Name = "figma_delete_slide")]
    [Description(ToolDescriptions.Slides.DeleteSlide)]
    public Task<string> DeleteSlideAsync(
        [Description("Id of the slide to delete.")] string slideId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.DeleteSlide, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
        }, cancellationToken);

    [McpServerTool(Name = "figma_duplicate_slide")]
    [Description(ToolDescriptions.Slides.DuplicateSlide)]
    public Task<string> DuplicateSlideAsync(
        [Description("Slide id.")] string slideId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.DuplicateSlide, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
        }, cancellationToken);

    [McpServerTool(Name = "figma_reorder_slides")]
    [Description(ToolDescriptions.Slides.ReorderSlides)]
    public Task<string> ReorderSlidesAsync(
        [Description("New grid as a JSON string.")] string grid,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(grid, out var parsed, out var error) || parsed is not JsonArray rows)
            {
                return ToolResponse.Error(
                    $"grid did not parse as a JSON array: {error ?? "the value is not an array"}",
                    "An array of grid rows is expected: [[\"1:2\",\"1:3\"],[\"1:4\"]].");
            }

            if (rows.Count > MaxGridRows || rows.Any(row => row is JsonArray items && items.Count > MaxGridRow))
            {
                return ToolResponse.Error(
                    $"The grid is larger than allowed: at most {MaxGridRows} rows of {MaxGridRow} slides.");
            }

            var result = await Plugin.ApplyAsync(
                EditorCommands.ReorderSlides,
                new JsonObject { ["grid"] = rows.DeepClone() },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_slide_transition")]
    [Description(ToolDescriptions.Slides.SetSlideTransition)]
    public Task<string> SetSlideTransitionAsync(
        [Description("Slide id.")] string slideId,
        [Description("Transition style.")] string style,
        [Description("Duration in seconds, from 0.01 to 10.")] double duration = 0.4,
        [Description("Easing curve.")] string curve = "EASE_IN_AND_OUT",
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.SetSlideTransition, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
            ["style"] = style,
            ["duration"] = Math.Clamp(duration, 0.01, 10),
            ["curve"] = curve,
        }, cancellationToken);

    [McpServerTool(Name = "figma_skip_slide")]
    [Description(ToolDescriptions.Slides.SkipSlide)]
    public Task<string> SkipSlideAsync(
        [Description("Slide id.")] string slideId,
        [Description("true skips it, false shows it.")] bool skip,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.SkipSlide, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
            ["skip"] = skip,
        }, cancellationToken);

    [McpServerTool(Name = "figma_add_text_to_slide")]
    [Description(ToolDescriptions.Slides.AddTextToSlide)]
    public Task<string> AddTextToSlideAsync(
        [Description("Slide id.")] string slideId,
        [Description("Text.")] string text,
        [Description("X coordinate.")] double x = 100,
        [Description("Y coordinate.")] double y = 100,
        [Description("Font size in pixels.")] double fontSize = 24,
        [Description("Font family, for example 'Manrope', 'IBM Plex Sans'.")] string fontFamily = "Inter",
        [Description("Font style, for example 'Bold', 'SemiBold', 'Medium'.")] string fontStyle = "Regular",
        [Description("Text color in hex, for example '#FFFFFF'.")] string? color = null,
        [Description("Alignment: LEFT, CENTER, RIGHT.")] string? textAlign = null,
        [Description("Width of the text block — it enables line wrapping.")] double? width = null,
        [Description("Line height in pixels.")] double? lineHeight = null,
        [Description("Letter spacing in pixels.")] double? letterSpacing = null,
        [Description("Case: ORIGINAL, UPPER, LOWER, TITLE.")] string? textCase = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.AddTextToSlide, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
            ["text"] = text,
            ["x"] = x,
            ["y"] = y,
            ["fontSize"] = fontSize,
            ["fontFamily"] = fontFamily,
            ["fontStyle"] = fontStyle,
            ["color"] = color,
            ["textAlign"] = textAlign,
            ["width"] = width,
            ["lineHeight"] = lineHeight,
            ["letterSpacing"] = letterSpacing,
            ["textCase"] = textCase,
        }, cancellationToken);

    [McpServerTool(Name = "figma_add_shape_to_slide")]
    [Description(ToolDescriptions.Slides.AddShapeToSlide)]
    public Task<string> AddShapeToSlideAsync(
        [Description("Slide id.")] string slideId,
        [Description("Shape type: RECTANGLE or ELLIPSE.")] string shapeType,
        [Description("X coordinate.")] double x,
        [Description("Y coordinate.")] double y,
        [Description("Width in pixels.")] double width,
        [Description("Height in pixels.")] double height,
        [Description("Fill color in hex, for example '#FF5733'.")] string fillColor = "#CCCCCC",
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.AddShapeToSlide, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
            ["shapeType"] = shapeType,
            ["x"] = x,
            ["y"] = y,
            ["width"] = width,
            ["height"] = height,
            ["fillColor"] = fillColor,
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_slide_background")]
    [Description(ToolDescriptions.Slides.SetSlideBackground)]
    public Task<string> SetSlideBackgroundAsync(
        [Description("Slide id.")] string slideId,
        [Description("Background color in hex.")] string color,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.SetSlideBackground, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
            ["color"] = color,
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_slides_view_mode")]
    [Description(ToolDescriptions.Slides.SetSlidesViewMode)]
    public Task<string> SetSlidesViewModeAsync(
        [Description("View mode: grid or single-slide.")] string mode,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.SetSlidesViewMode, new JsonObject
        {
            ["mode"] = mode,
        }, cancellationToken);

    [McpServerTool(Name = "figma_focus_slide")]
    [Description(ToolDescriptions.Slides.FocusSlide)]
    public Task<string> FocusSlideAsync(
        [Description("Slide id.")] string slideId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.FocusSlide, new JsonObject
        {
            ["slideId"] = NodeId.Normalize(slideId),
        }, cancellationToken);
}
