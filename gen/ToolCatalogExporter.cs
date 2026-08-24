using System.Text.Json;
using System.Text.Json.Nodes;

namespace Snail.MCP.Figma.Documentation;

/// <summary>Writes the tool catalog into the MkDocs tree: one page per family, one index and the raw schemas.</summary>
public static class ToolCatalogExporter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static IReadOnlyList<string> Export(IReadOnlyList<ToolFamily> families, string outputDirectory)
    {
        var tools = Path.Combine(outputDirectory, "tools");
        var schemas = Path.Combine(outputDirectory, "schemas");

        Directory.CreateDirectory(tools);
        Directory.CreateDirectory(schemas);

        var written = new List<string> { Write(Path.Combine(tools, "index.md"), MarkdownPages.Index(families)) };

        foreach (var family in families)
        {
            written.Add(Write(Path.Combine(tools, $"{family.Slug}.md"), MarkdownPages.Family(family)));
        }

        written.Add(Write(Path.Combine(schemas, "tools.json"), Schemas(families)));

        return written;
    }

    private static string Schemas(IReadOnlyList<ToolFamily> families)
    {
        var array = new JsonArray();

        foreach (var tool in families.SelectMany(family => family.Tools))
        {
            array.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
            });
        }

        return array.ToJsonString(Indented);
    }

    private static string Write(string path, string content)
    {
        File.WriteAllText(path, content);

        return path;
    }
}
