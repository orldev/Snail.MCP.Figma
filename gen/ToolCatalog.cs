using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Documentation;

/// <summary>One tool exactly as the model receives it in tools/list.</summary>
public sealed record ToolEntry(string Name, string Description, JsonElement InputSchema);

/// <summary>Tools that share a declaring class and therefore a subject.</summary>
public sealed record ToolFamily(string Slug, string Title, IReadOnlyList<ToolEntry> Tools);

/// <summary>Reads the tool catalog through the SDK, so the pages carry the same names, descriptions and schemas the server sends over the wire.</summary>
public static class ToolCatalog
{
    private static readonly (string Type, string Slug, string Title)[] Families =
    [
        ("ReadTools", "reading", "Reading"),
        ("WriteTools", "writing", "Writing"),
        ("NodeTools", "nodes", "Nodes"),
        ("ComponentTools", "components", "Components"),
        ("VariableTools", "variables", "Variables"),
        ("TokenTools", "design-tokens", "Design tokens"),
        ("DesignCodeTools", "design-to-code", "Design to code"),
        ("DesignSystemKitTools", "design-system-kit", "Design system kit"),
        ("DesignSystemExtractionTools", "design-system-from-code", "Design system from code"),
        ("VersionTools", "version-history", "Version history"),
        ("CommentTools", "comments", "Comments"),
        ("LibraryTools", "libraries", "Libraries"),
        ("FigJamTools", "figjam", "FigJam"),
        ("SlidesTools", "slides", "Slides"),
        ("SlotTools", "slots", "Slots"),
        ("QualityTools", "quality", "Quality and annotations"),
        ("SessionTools", "session", "Session and console"),
        ("DiagnosticsTools", "diagnostics", "Diagnostics"),
    ];

    public static IReadOnlyList<ToolFamily> Read()
    {
        var declared = typeof(ToolDescriptions).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .ToDictionary(type => type.Name, Describe, StringComparer.Ordinal);

        var known = Families.Select(family => family.Type).ToHashSet(StringComparer.Ordinal);
        var missing = declared.Keys.Where(name => !known.Contains(name)).ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"these tool classes have no family in ToolCatalog.Families: {string.Join(", ", missing)}");
        }

        return Families
            .Where(family => declared.ContainsKey(family.Type))
            .Select(family => new ToolFamily(family.Slug, family.Title, declared[family.Type]))
            .ToList();
    }

    private static IReadOnlyList<ToolEntry> Describe(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .OrderBy(method => method.MetadataToken)
            .Select(Protocol)
            .Select(tool => new ToolEntry(tool.Name, tool.Description ?? string.Empty, tool.InputSchema))
            .ToList();

    private static Tool Protocol(MethodInfo method) =>
        McpServerTool.Create(method, Unreachable, new McpServerToolCreateOptions()).ProtocolTool;

    private static object Unreachable(RequestContext<CallToolRequestParams> context) =>
        throw new NotSupportedException("the exporter reads declarations only and never invokes a tool");
}
