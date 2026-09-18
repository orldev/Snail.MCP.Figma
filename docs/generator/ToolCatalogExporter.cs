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

        RemoveStale(tools, written);

        return written;
    }

    /// <summary>Deletes the pages of families that no longer exist: a page left behind would still be built and found by the search, with tools the server no longer has.</summary>
    private static void RemoveStale(string tools, IReadOnlyCollection<string> written)
    {
        var current = written.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);

        foreach (var page in Directory.EnumerateFiles(tools, "*.md").Where(page => !current.Contains(Path.GetFullPath(page))).ToList())
        {
            File.Delete(page);
        }
    }

    private static string Schemas(IReadOnlyList<ToolFamily> families)
    {
        var array = new JsonArray();

        foreach (var tool in families.SelectMany(family => family.Tools))
        {
            var entry = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
            };

            if (tool.OutputSchema is { } output)
            {
                entry["outputSchema"] = JsonNode.Parse(output.GetRawText());
            }

            if (tool.Annotations is { } hints)
            {
                entry["annotations"] = new JsonObject
                {
                    ["readOnlyHint"] = hints.ReadOnlyHint,
                    ["destructiveHint"] = hints.DestructiveHint,
                    ["idempotentHint"] = hints.IdempotentHint,
                    ["openWorldHint"] = hints.OpenWorldHint,
                };
            }

            array.Add(entry);
        }

        return array.ToJsonString(Indented);
    }

    private static string Write(string path, string content)
    {
        File.WriteAllText(path, content);

        return path;
    }
}
