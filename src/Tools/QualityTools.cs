using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Design quality checks: linter and accessibility audit.</summary>
[McpServerToolType]
public sealed class QualityTools(ILiveEditor plugin, ILogger<QualityTools> logger)
    : EditorToolBase(plugin, logger)
{
    [McpServerTool(Name = "figma_lint_design")]
    [Description(ToolDescriptions.Quality.LintDesign)]
    public Task<string> LintDesignAsync(
        [Description("Id of the root node. Empty means the whole current page.")] string? nodeId = null,
        [Description("Comma-separated rules.")] string? rules = null,
        [Description("Maximum traversal depth.")] int maxDepth = 10,
        [Description("Cap on the number of findings.")] int maxFindings = 100,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.LintDesign, new JsonObject
        {
            ["nodeId"] = nodeId is null ? null : NodeId.Normalize(nodeId),
            ["rules"] = SplitRules(rules),
            ["maxDepth"] = maxDepth,
            ["maxFindings"] = maxFindings,
        }, cancellationToken, TimeSpan.FromSeconds(60));

    [McpServerTool(Name = "figma_audit_component_accessibility")]
    [Description(ToolDescriptions.Quality.AuditComponentAccessibility)]
    public Task<string> AuditAccessibilityAsync(
        [Description("Id of the component or frame.")] string nodeId,
        [Description("Minimum size of tap targets in pixels.")] int targetSize = 44,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.AuditComponentAccessibility, new JsonObject
        {
            ["nodeId"] = NodeId.Normalize(nodeId),
            ["targetSize"] = targetSize,
        }, cancellationToken, TimeSpan.FromSeconds(60));

    [McpServerTool(Name = "figma_get_annotations")]
    [Description(ToolDescriptions.Quality.GetAnnotations)]
    public Task<string> GetAnnotationsAsync(
        [Description("Node id. Empty means the whole page.")] string? nodeId = null,
        CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetAnnotations, new JsonObject
        {
            ["nodeId"] = nodeId is null ? null : NodeId.Normalize(nodeId),
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_annotations")]
    [Description(ToolDescriptions.Quality.SetAnnotations)]
    public Task<string> SetAnnotationsAsync(
        [Description("Node id.")] string nodeId,
        [Description("Array of annotations as a JSON string.")] string annotations,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(annotations, out var parsed, out var error) || parsed is null)
            {
                return ToolResponse.Error($"annotations did not parse as JSON: {error}",
                    "An array is expected, for example [{\"label\":\"Spacing 16\"}].");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.SetAnnotations, new JsonObject
            {
                ["nodeId"] = NodeId.Normalize(nodeId),
                ["annotations"] = parsed,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_annotation_categories")]
    [Description(ToolDescriptions.Quality.GetAnnotationCategories)]
    public Task<string> GetAnnotationCategoriesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetAnnotationCategories, null, cancellationToken);

    [McpServerTool(Name = "figma_add_annotation_category")]
    [Description(ToolDescriptions.Quality.AddAnnotationCategory)]
    public Task<string> AddAnnotationCategoryAsync(
        [Description("Label of the category.")] string label,
        [Description("Color: yellow, orange, red, pink, violet, blue, teal or green.")] string color,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var normalized = color.Trim().ToLowerInvariant();

            if (normalized is not ("yellow" or "orange" or "red" or "pink" or "violet" or "blue" or "teal" or "green"))
            {
                return ToolResponse.Error(
                    $"Unknown color '{color}'.",
                    "Figma accepts yellow, orange, red, pink, violet, blue, teal or green.");
            }

            var script = $$"""
                if (!figma.annotations || typeof figma.annotations.addAnnotationCategoryAsync !== 'function') {
                  return { __error: 'figma.annotations API not available. Requires a recent Figma Desktop build.' };
                }
                const category = await figma.annotations.addAnnotationCategoryAsync({
                  label: {{JsonSerializer.Serialize(label)}},
                  color: {{JsonSerializer.Serialize(normalized)}},
                });
                return { id: category.id, label: category.label, color: category.color, isPreset: category.isPreset === true };
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

    private static JsonArray? SplitRules(string? rules)
    {
        if (string.IsNullOrWhiteSpace(rules))
        {
            return null;
        }

        var items = rules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return items.Length == 0 ? null : new JsonArray(items.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
    }
}
