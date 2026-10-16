using System.Text.RegularExpressions;
using Snail.MCP.Figma.Adapters.Editor;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>Editor read scripts are embedded resources: a missing file or a leftover placeholder would surface only inside the plugin at runtime.</summary>
public partial class EditorScriptsTests
{
    [Fact]
    public void EditorScripts_EveryResourceName_IsEmbeddedInTheAssembly()
    {
        foreach (var name in EditorScripts.ResourceNames)
        {
            Assert.False(string.IsNullOrWhiteSpace(EditorScripts.Load(name)), name);
        }
    }

    [Fact]
    public void EditorScripts_ScriptsRendered_LeaveNoPlaceholdersBehind()
    {
        foreach (var script in new[] { EditorScripts.Subtree("1:2", 3, 500), EditorScripts.FileTree(2, 100), EditorScripts.Styles })
        {
            Assert.DoesNotMatch(Placeholder, script);
        }
    }

    [Fact]
    public void EditorScripts_HostileNodeId_IsEmbeddedAsAJsonLiteral()
    {
        var script = EditorScripts.Subtree("I1:2;3:4\"; figma.root.remove(); var x = \"", 1, 10);

        Assert.Contains("var __id = \"I1:2;3:4\\u0022; figma.root.remove(); var x = \\u0022\";", script);
    }

    [Fact]
    public void EditorScripts_SubtreeAndFileTree_ShareTheSerializer()
    {
        Assert.Contains("function __ser(", EditorScripts.Subtree("1:2", 1, 10));
        Assert.Contains("function __ser(", EditorScripts.FileTree(1, 10));
        Assert.Contains("loadAllPagesAsync", EditorScripts.FileTree(2, 10));
    }

    [GeneratedRegex("__[A-Z_]+__")]
    private static partial Regex Placeholder { get; }
}
