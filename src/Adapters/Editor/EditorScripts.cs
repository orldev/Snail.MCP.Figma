using System.Collections.Concurrent;
using System.Reflection;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>JavaScript for reads through the editor, stored as embedded <c>Scripts/*.js</c> with <c>__NAME__</c> placeholders.</summary>
internal static class EditorScripts
{
    private static readonly ConcurrentDictionary<string, string> Loaded = new(StringComparer.Ordinal);

    public static readonly string[] ResourceNames = ["serializer.js", "subtree.js", "file-tree.js", "styles.js"];

    /// <summary>Subtree of one node; the id is embedded as a JSON literal so it stays data.</summary>
    public static string Subtree(string nodeId, int depth, int maxNodes) =>
        Load("serializer.js") + Render("subtree.js", new()
        {
            ["__NODE_ID__"] = JsonValue.Create(nodeId).ToJsonString(),
            ["__DEPTH__"] = depth.ToString(),
            ["__MAX_NODES__"] = maxNodes.ToString(),
        });

    /// <summary>File tree from the root; depth follows REST semantics (1 = pages only).</summary>
    public static string FileTree(int depth, int maxNodes) =>
        Load("serializer.js") + Render("file-tree.js", new()
        {
            ["__DEPTH__"] = depth.ToString(),
            ["__MAX_NODES__"] = maxNodes.ToString(),
        });

    /// <summary>Local styles in the published-source shape.</summary>
    public static string Styles => Load("styles.js");

    internal static string Render(string resource, Dictionary<string, string> values)
    {
        var script = Load(resource);

        foreach (var (placeholder, value) in values)
        {
            script = script.Replace(placeholder, value, StringComparison.Ordinal);
        }

        return script;
    }

    internal static string Load(string resource) => Loaded.GetOrAdd(resource, static name =>
    {
        var assembly = typeof(EditorScripts).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(candidate => candidate.EndsWith($".{name}", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Script {name} is not embedded in the assembly; check the EmbeddedResource items in the csproj.");

        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);

        return $"{reader.ReadToEnd()}\n";
    });
}
