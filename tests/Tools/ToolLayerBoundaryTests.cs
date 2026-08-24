using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Ports &amp; Adapters boundary: the Tools layer is the MCP driving adapter and must not see WebSocket, HTTP, LiteDB or concrete Figma adapters.</summary>
public class ToolLayerBoundaryTests
{
    private static readonly string[] ForbiddenNamespaces = ["System.Net", "LiteDB", "Snail.MCP.Figma.Hosting", "Snail.MCP.Figma.Extensions", "Snail.MCP.Figma.Adapters"];

    private static readonly string[] ForbiddenTypes =
        [
            "EditorSocketHub", "EditorCommandRouter", "LiveEditorGateway", "OpenDocumentRegistry", "EditorSession",
            "EditorWireProtocol", "EditorScripts", "CaptureStore", "EditorExtensionInstaller",
            "RestDocumentGateway", "QuotaGate", "LiteDatabaseProvider", "ILiteDatabaseProvider", "HttpClient",
        ];

    [Fact]
    public void ToolsDependOnlyOnPortsThroughTheirConstructors()
    {
        var offenders = typeof(ToolDescriptions).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetConstructors().SelectMany(ctor => ctor.GetParameters())
                .Select(parameter => (Tool: type.Name, Dependency: parameter.ParameterType)))
            .Where(pair => IsForbidden(pair.Dependency))
            .Select(pair => $"{pair.Tool} ← {pair.Dependency.FullName}")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, $"infrastructure in tool constructors: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ToolSourcesDoNotMentionInfrastructureTypes()
    {
        var toolsDirectory = Path.Combine(FindRepositoryRoot(), "src", "Tools");
        var pattern = new Regex(
            $@"\b({string.Join("|", ForbiddenNamespaces.Concat(ForbiddenTypes).Select(Regex.Escape))})\b",
            RegexOptions.Compiled);

        var offenders = Directory.EnumerateFiles(toolsDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(entry => !entry.Line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .Where(entry => pattern.IsMatch(entry.Line))
                .Select(entry => $"{Path.GetRelativePath(toolsDirectory, file)}:{entry.Number}: {entry.Line.Trim()}"))
            .ToList();

        Assert.True(offenders.Count == 0, $"infrastructure in Tools sources:\n{string.Join("\n", offenders)}");
    }

    private static bool IsForbidden(Type type) =>
        ForbiddenNamespaces.Any(ns => type.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true)
        || ForbiddenTypes.Contains(type.Name, StringComparer.Ordinal);

    /// <summary>Walks up from bin/ to the directory containing src/Tools, independent of the runner's working directory.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"src/Tools not found above {AppContext.BaseDirectory}");
    }
}
