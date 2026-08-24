using System.Text;

namespace Snail.MCP.Figma.Application.Extraction;

/// <summary>Component documentation in Markdown, built from design data.</summary>
public static class ComponentDoc
{
    public static string Render(JsonNode node, IReadOnlyList<ExtractedStyle> styles, JsonObject? codeInfo)
    {
        var name = node["name"]?.GetValue<string>() ?? "Component";
        var builder = new StringBuilder();

        builder.AppendLine($"# {name}");
        builder.AppendLine();

        var description = node["description"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine(description.Trim()).AppendLine();
        }

        AppendAnatomy(builder, node);
        AppendVariants(builder, node);
        AppendProperties(builder, node);
        AppendTokens(builder, styles);
        AppendCodeInfo(builder, codeInfo);

        return builder.ToString();
    }

    private static void AppendAnatomy(StringBuilder builder, JsonNode node)
    {
        if (node["children"] is not JsonArray children || children.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Contents").AppendLine();
        AppendChildren(builder, children, depth: 0);
        builder.AppendLine();
    }

    private static void AppendChildren(StringBuilder builder, JsonArray children, int depth)
    {
        if (depth > 2)
        {
            return;
        }

        foreach (var child in children)
        {
            var name = child?["name"]?.GetValue<string>() ?? "—";
            var type = child?["type"]?.GetValue<string>() ?? "?";
            builder.AppendLine($"{new string(' ', depth * 2)}- **{name}** ({type})");

            if (child?["children"] is JsonArray grandChildren)
            {
                AppendChildren(builder, grandChildren, depth + 1);
            }
        }
    }

    private static void AppendVariants(StringBuilder builder, JsonNode node)
    {
        var variants = ReconstructionSpec.ListVariants(node);

        if (variants.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Variants").AppendLine();

        foreach (var variant in variants)
        {
            builder.AppendLine($"- `{variant}`");
        }

        builder.AppendLine();
    }

    private static void AppendProperties(StringBuilder builder, JsonNode node)
    {
        if (node["componentPropertyDefinitions"] is not JsonObject definitions || definitions.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Properties").AppendLine();
        builder.AppendLine("| Property | Type | Default |");
        builder.AppendLine("|---|---|---|");

        foreach (var (property, definition) in definitions)
        {
            builder.AppendLine(
                $"| {property} | {definition?["type"]?.GetValue<string>() ?? "—"} " +
                $"| {definition?["defaultValue"]?.ToJsonString() ?? "—"} |");
        }

        builder.AppendLine();
    }

    private static void AppendTokens(StringBuilder builder, IReadOnlyList<ExtractedStyle> styles)
    {
        if (styles.Count == 0)
        {
            return;
        }

        foreach (var group in styles.GroupBy(s => s.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"## {TitleFor(group.Key)}").AppendLine();

            foreach (var style in group.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                builder.AppendLine($"- `{style.Name}` — `{style.Value}`");
            }

            builder.AppendLine();
        }
    }

    private static void AppendCodeInfo(StringBuilder builder, JsonObject? codeInfo)
    {
        if (codeInfo is null || codeInfo.Count == 0)
        {
            return;
        }

        builder.AppendLine("## From the code side").AppendLine();

        foreach (var (key, value) in codeInfo)
        {
            builder.AppendLine($"- **{key}**: `{value?.ToJsonString()}`");
        }

        builder.AppendLine();
    }

    private static string TitleFor(string type) => type switch
    {
        "COLOR" => "Colors",
        "TYPOGRAPHY" => "Typography",
        "SPACING" => "Spacing",
        "RADIUS" => "Radii",
        _ => type,
    };
}
