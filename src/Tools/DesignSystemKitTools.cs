using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Extraction;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Design-system kit in one call: tokens, components and styles, with delta-encoded variant specs and tiered compression.</summary>
[McpServerToolType]
public sealed class DesignSystemKitTools(
    ILiveEditor plugin,
    KitAssembly kits,
    ActiveDocumentTracker activeFile,
    ILogger<DesignSystemKitTools> logger)
    : EditorToolBase(plugin, logger)
{
    /// <summary>Returns the tokens, components and styles of the file in one response compressed to fit the size of the system.</summary>
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
            var request = new KitRequest(activeFile.Resolve(fileUrl), ParseSections(include), ParseIds(componentIds), includeImages, format);
            var kit = await kits.AssembleAsync(request, ct).ConfigureAwait(false);

            return ToolResponse.Ok(kit);
        }, cancellationToken);

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
            : [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(NodeId.Normalize)];
}
