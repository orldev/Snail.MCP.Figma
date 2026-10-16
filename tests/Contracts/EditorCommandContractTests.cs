using System.Reflection;
using System.Text.RegularExpressions;
using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Contracts;

/// <summary>Every EditorCommands constant must have a handler in the plugin bundle — a missing one surfaces at runtime only as a timeout.</summary>
public partial class EditorCommandContractTests
{
    /// <summary>Handlers the plugin UI triggers on its own; no server command maps to them.</summary>
    private static readonly HashSet<string> PluginInternal = ["RESIZE_UI"];

    [Fact]
    public void EditorCommands_EveryCommand_HasAHandlerInTheBundle()
    {
        var handled = CodeJsHandlers().Concat(UiDispatcherHandlers()).ToHashSet(StringComparer.Ordinal);
        var missing = Commands().Where(command => !handled.Contains(command)).ToList();

        Assert.True(missing.Count == 0,
            $"EditorCommands the server can send but neither plugin/code.js nor the ui.html dispatcher handles: {string.Join(", ", missing)}. " +
            "Add a handler to the bundle, or the command will always time out.");
    }

    [Fact]
    public void EditorCommands_EveryCodeJsHandler_IsReachableFromTheServer()
    {
        var commands = Commands().ToHashSet(StringComparer.Ordinal);
        var orphans = CodeJsHandlers()
            .Where(handler => !commands.Contains(handler) && !PluginInternal.Contains(handler))
            .ToList();

        Assert.True(orphans.Count == 0,
            $"plugin/code.js handlers no EditorCommands constant sends: {string.Join(", ", orphans)}. " +
            "Dead code — remove the handler, or register the command in EditorCommands.");
    }

    /// <summary>Wire names of every command the server knows.</summary>
    private static IReadOnlyList<string> Commands() =>
        [.. typeof(EditorCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(EditorCommand))
            .Select(field => ((EditorCommand)field.GetValue(null)!).Name)];

    /// <summary>Command names handled by the message chain in plugin/code.js.</summary>
    private static IReadOnlyList<string> CodeJsHandlers()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "plugin", "code.js"));

        return [.. MessageTypePattern.Matches(source)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Commands served directly by the iframe dispatcher in plugin/ui.html, never reaching code.js.</summary>
    private static IReadOnlyList<string> UiDispatcherHandlers()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "plugin", "ui.html"));

        return [.. CommandHandlerPattern.Matches(source)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "plugin")))
            {
                return directory.FullName;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("the plugin folder was not found above the test binaries");
    }

    [GeneratedRegex(@"msg\.type === '([A-Z_]+)'")]
    private static partial Regex MessageTypePattern { get; }

    [GeneratedRegex(@"'([A-Z_]+)':\s*function")]
    private static partial Regex CommandHandlerPattern { get; }
}
