using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>The instructions are the only text a discovering host shows before any tool loads, so they must name every family, point only at real tools and stay short enough to sit in every context.</summary>
public class ServerInstructionsTests
{
    /// <summary>About 1,000 tokens: a ceiling, not a target — the instructions ride in every request of every session that mounts the server.</summary>
    private const int MaxCharacters = 4500;

    private static readonly Regex ToolReference = new(@"\b(figma|figjam|csharp)_[a-z0-9_]+", RegexOptions.Compiled);

    private static IReadOnlyList<Type> ToolTypes() =>
        typeof(ToolDescriptions).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .ToList();

    private static IReadOnlyList<string> ToolNames(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToList();

    [Fact]
    public void EveryToolFamilyIsIntroducedByAtLeastOneOfItsTools()
    {
        var silent = ToolTypes()
            .Where(type => !ToolNames(type).Any(name => ServerInstructions.Text.Contains(name, StringComparison.Ordinal)))
            .Select(type => type.Name)
            .ToList();

        Assert.True(silent.Count == 0, $"tool classes the instructions never mention: {string.Join(", ", silent)}");
    }

    [Fact]
    public void InstructionsReferenceOnlyExistingTools()
    {
        var known = ToolTypes().SelectMany(ToolNames).ToHashSet(StringComparer.Ordinal);

        var dangling = ToolReference.Matches(ServerInstructions.Text)
            .Select(match => match.Value)
            .Where(reference => !known.Contains(reference))
            .Distinct()
            .ToList();

        Assert.True(dangling.Count == 0, $"references to non-existent tools: {string.Join(", ", dangling)}");
    }

    [Fact]
    public void InstructionsStateTheToolCountTheServerActuallyHas()
    {
        var count = ToolTypes().Sum(type => ToolNames(type).Count);

        Assert.Contains($"{count} tools", ServerInstructions.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void InstructionsStayUnderTheContextCeiling()
    {
        Assert.InRange(ServerInstructions.Text.Length, 1, MaxCharacters);
    }
}
