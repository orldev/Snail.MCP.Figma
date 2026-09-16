using System.Text;
using System.Text.Json;

namespace Snail.MCP.Figma.Documentation;

/// <summary>Renders the catalog as Markdown for MkDocs.</summary>
public static class MarkdownPages
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Index(IReadOnlyList<ToolFamily> families)
    {
        var total = families.Sum(family => family.Tools.Count);
        var page = new StringBuilder()
            .AppendLine("# Tools")
            .AppendLine()
            .AppendLine($"{total} tools in {families.Count} families. The names, descriptions and schemas below are")
            .AppendLine("generated from the server itself, so they are exactly what the model receives in `tools/list`.")
            .AppendLine()
            .AppendLine("| Tool | Family | What it does |")
            .AppendLine("|------|--------|--------------|");

        foreach (var family in families)
        {
            foreach (var tool in family.Tools)
            {
                page.AppendLine($"| [`{tool.Name}`]({family.Slug}.md#{tool.Name}) | {family.Title} | {Cell(FirstSentence(tool.Description))} |");
            }
        }

        return page.ToString();
    }

    public static string Family(ToolFamily family)
    {
        var page = new StringBuilder()
            .AppendLine($"# {family.Title}")
            .AppendLine();

        foreach (var tool in family.Tools)
        {
            page.AppendLine($"## <code>{Seams(tool.Name)}</code>")
                .AppendLine()
                .AppendLine(tool.Description)
                .AppendLine();

            AppendParameters(page, tool.InputSchema);

            page.AppendLine("<details><summary>JSON Schema</summary>")
                .AppendLine()
                .AppendLine("```json")
                .AppendLine(JsonSerializer.Serialize(tool.InputSchema, Indented))
                .AppendLine("```")
                .AppendLine()
                .AppendLine("</details>")
                .AppendLine();
        }

        return page.ToString();
    }

    private static void AppendParameters(StringBuilder page, JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties) || !properties.EnumerateObject().Any())
        {
            page.AppendLine("Takes no parameters.").AppendLine();

            return;
        }

        var required = schema.TryGetProperty("required", out var names)
            ? names.EnumerateArray().Select(name => name.GetString()).ToHashSet(StringComparer.Ordinal)
            : [];

        page.AppendLine("| Parameter | Type | Required | Default | Description |")
            .AppendLine("|-----------|------|----------|---------|-------------|");

        foreach (var property in properties.EnumerateObject())
        {
            var description = property.Value.TryGetProperty("description", out var text) ? text.GetString() ?? string.Empty : string.Empty;
            page.AppendLine(
                $"| `{property.Name}` | {TypeOf(property.Value)} | {(required.Contains(property.Name) ? "yes" : "—")} " +
                $"| {DefaultOf(property.Value)} | {Cell(description)} |");
        }

        page.AppendLine();
    }

    private static string TypeOf(JsonElement property)
    {
        if (property.TryGetProperty("enum", out var choices))
        {
            return string.Join(" \\| ", choices.EnumerateArray().Select(choice => $"`{choice}`"));
        }

        if (!property.TryGetProperty("type", out var type))
        {
            return "`any`";
        }

        if (type.ValueKind is JsonValueKind.String)
        {
            return $"`{type.GetString()}`";
        }

        var names = type.EnumerateArray().Select(name => name.GetString()).ToList();
        var nullable = names.Remove("null");

        return $"`{string.Join(" | ", names)}{(nullable ? "?" : string.Empty)}`";
    }

    private static string DefaultOf(JsonElement property)
    {
        if (!property.TryGetProperty("default", out var value))
        {
            return "—";
        }

        return value.ValueKind is JsonValueKind.String ? $"`{value.GetString()}`" : $"`{value.GetRawText()}`";
    }

    private static string FirstSentence(string description)
    {
        var end = description.IndexOf(". ", StringComparison.Ordinal);
        var sentence = end < 0 ? description : description[..(end + 1)];

        return sentence.Length <= 160 ? sentence : $"{sentence[..157]}…";
    }

    /// <summary>The name with a line-break opportunity after every underscore: the heading sits in a 152-pixel margin and would otherwise break mid-word.
    /// A &lt;wbr&gt; carries no character, so the anchor, the search index and a copied name all stay the plain name.</summary>
    private static string Seams(string name) => name.Replace("_", "_<wbr>", StringComparison.Ordinal);

    private static string Cell(string text) =>
        text.Replace("|", "\\|").ReplaceLineEndings(" ").Trim();
}
