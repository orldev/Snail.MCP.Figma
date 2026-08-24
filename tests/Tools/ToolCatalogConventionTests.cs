using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Prompt catalog guards: descriptions are hardcoded consts that drift silently as the code evolves; these tests catch drift at build time.</summary>
public class ToolCatalogConventionTests
{
    private static readonly Regex ToolNamePattern = new("^(figma|figjam|csharp)_[a-z0-9_]+$", RegexOptions.Compiled);

    private static readonly Regex ToolReference = new(@"\b(figma|figjam|csharp)_[a-z0-9_]+", RegexOptions.Compiled);

    private static IReadOnlyList<MethodInfo> ToolMethods() =>
        typeof(ToolDescriptions).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

    private static string ToolName(MethodInfo method) => method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;

    private static string? DescriptionOf(ICustomAttributeProvider member) =>
        member.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;

    /// <summary>Catalog constants except the Parameters group, which holds parameter descriptions.</summary>
    private static Dictionary<string, string> CatalogEntries() =>
        typeof(ToolDescriptions).GetNestedTypes()
            .Where(group => group.Name != nameof(ToolDescriptions.Parameters))
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => $"{field.DeclaringType!.Name}.{field.Name}", field => (string)field.GetRawConstantValue()!);

    [Fact]
    public void EveryToolTakesItsDescriptionFromTheCatalog()
    {
        var catalog = CatalogEntries().Values.ToHashSet(StringComparer.Ordinal);

        var offenders = ToolMethods()
            .Where(method => DescriptionOf(method) is not { Length: > 0 } description || !catalog.Contains(description))
            .Select(ToolName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"description missing or not from ToolDescriptions: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void CatalogHasNoOrphanedEntries()
    {
        var used = ToolMethods().Select(DescriptionOf).ToHashSet(StringComparer.Ordinal);

        var orphans = CatalogEntries()
            .Where(entry => !used.Contains(entry.Value))
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(orphans.Count == 0, $"constants without a tool: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void EveryParameterIsDescribed()
    {
        var offenders = ToolMethods()
            .SelectMany(method => method.GetParameters()
                .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                .Where(parameter => string.IsNullOrWhiteSpace(DescriptionOf(parameter)))
                .Select(parameter => $"{ToolName(method)}({parameter.Name})"))
            .ToList();

        Assert.True(offenders.Count == 0, $"parameters without a description: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ToolNamesFollowThePrefixConventionAndAreUnique()
    {
        var names = ToolMethods().Select(ToolName).ToList();

        Assert.All(names, name => Assert.Matches(ToolNamePattern, name));
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DescriptionsReferenceOnlyExistingTools()
    {
        var known = ToolMethods().Select(ToolName).ToHashSet(StringComparer.Ordinal);

        var dangling = ToolMethods()
            .SelectMany(method => ToolReference.Matches(DescriptionOf(method) ?? string.Empty)
                .Select(match => match.Value)
                .Where(reference => !known.Contains(reference))
                .Select(reference => $"{ToolName(method)} → {reference}"))
            .Distinct()
            .ToList();

        Assert.True(dangling.Count == 0, $"references to non-existent tools: {string.Join(", ", dangling)}");
    }

    [Fact]
    public void SourceParameterMentionsEveryReadSource()
    {
        var expected = Enum.GetNames<ReadSource>().Select(name => name.ToLowerInvariant()).ToList();

        var parameters = ToolMethods()
            .SelectMany(method => method.GetParameters().Where(parameter => parameter.Name == "source"))
            .ToList();

        Assert.NotEmpty(parameters);
        Assert.All(parameters, parameter =>
        {
            var description = DescriptionOf(parameter) ?? string.Empty;
            Assert.All(expected, value => Assert.Contains(value, description, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ProfileParameterMentionsEverySanitizationProfile()
    {
        var expected = Enum.GetNames<SanitizationProfile>().Select(name => name.ToLowerInvariant()).ToList();

        var parameters = ToolMethods()
            .SelectMany(method => method.GetParameters().Where(parameter => parameter.Name == "profile"))
            .ToList();

        Assert.NotEmpty(parameters);
        Assert.All(parameters, parameter =>
        {
            var description = DescriptionOf(parameter) ?? string.Empty;
            Assert.All(expected, value => Assert.Contains(value, description, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void PluginViewDepthLimitIsStatedInTheDescription()
    {
        var method = Assert.Single(ToolMethods(), m => ToolName(m) == "figma_get_file_for_plugin");
        var depth = Assert.Single(method.GetParameters(), parameter => parameter.Name == "depth");

        Assert.Contains(
            ToolLimits.MaxPluginViewDepth.ToString(),
            DescriptionOf(depth) ?? string.Empty,
            StringComparison.Ordinal);
    }
}
