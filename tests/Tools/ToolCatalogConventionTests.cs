using Snail.Sdk.Mcp.Hosting;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Prompt catalog guards: descriptions are hardcoded consts that drift silently as the code evolves; these tests catch drift at build time.</summary>
public partial class ToolCatalogConventionTests
{
    private static IReadOnlyList<MethodInfo> ToolMethods() =>
        [.. typeof(ToolDescriptions).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)];

    private static string ToolName(MethodInfo method) => method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;

    /// <summary>The tool exactly as tools/list sends it; the SDK emits a hint only when the attribute states it, so a null hint here means the declaration is silent.</summary>
    private static Tool Protocol(MethodInfo method) =>
        McpServerTool.Create(method, Unreachable, new McpServerToolCreateOptions { SerializerOptions = ToolJson.Options(ToolJsonContext.Default) }).ProtocolTool;

    private static object Unreachable(RequestContext<CallToolRequestParams> context) =>
        throw new NotSupportedException("the convention tests read declarations only and never invoke a tool");

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
    public void ToolCatalog_EveryTool_TakesDescriptionFromCatalog()
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
    public void ToolCatalog_CatalogEntries_AreAllUsedByATool()
    {
        var used = ToolMethods().Select(DescriptionOf).ToHashSet(StringComparer.Ordinal);

        var orphans = CatalogEntries()
            .Where(entry => !used.Contains(entry.Value))
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(orphans.Count == 0, $"constants without a tool: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void ToolCatalog_EveryParameter_IsDescribed()
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
    public void ToolCatalog_ToolNames_FollowPrefixAndAreUnique()
    {
        var names = ToolMethods().Select(ToolName).ToList();

        Assert.All(names, name => Assert.Matches(ToolNamePattern, name));
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ToolCatalog_ToolDescriptions_ReferenceOnlyExistingTools()
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
    public void ToolCatalog_SourceParameter_MentionsEveryReadSource()
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
    public void ToolCatalog_ProfileParameter_MentionsEverySanitizationProfile()
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
    public void ToolCatalog_DeliverParameter_NamesBothModesAndInlineCeiling()
    {
        var parameters = ToolMethods()
            .SelectMany(method => method.GetParameters().Where(parameter => parameter.Name == "deliver"))
            .ToList();

        Assert.NotEmpty(parameters);
        Assert.All(parameters, parameter =>
        {
            var description = DescriptionOf(parameter) ?? string.Empty;

            Assert.Contains("path", description, StringComparison.Ordinal);
            Assert.Contains("both", description, StringComparison.Ordinal);
            Assert.Contains($"{ImageLimits.MaxInlineMegabytes} MB", description, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ToolCatalog_PictureFileParameter_StatesFileCap()
    {
        var parameters = ToolMethods()
            .SelectMany(method => method.GetParameters().Where(parameter => parameter.Name == "filePath"))
            .ToList();

        Assert.NotEmpty(parameters);
        Assert.All(parameters, parameter => Assert.Contains(
            $"{ImageLimits.MaxFileMegabytes} MB",
            DescriptionOf(parameter) ?? string.Empty,
            StringComparison.Ordinal));

        var placement = Assert.Single(ToolMethods(), method => ToolName(method) == "figma_place_image");

        Assert.Contains(
            $"{ImageLimits.MaxPixelSide}x{ImageLimits.MaxPixelSide}",
            DescriptionOf(placement) ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCatalog_ExecuteSteps_StatesStepCapAndPauseCeiling()
    {
        var method = Assert.Single(ToolMethods(), m => ToolName(m) == "figma_execute_steps");
        var pause = Assert.Single(method.GetParameters(), parameter => parameter.Name == "delayMs");

        Assert.Contains(
            $"{ToolLimits.MaxSteps}",
            DescriptionOf(method) ?? string.Empty,
            StringComparison.Ordinal);

        Assert.Contains(
            $"{ToolLimits.MaxStepDelayMs}",
            DescriptionOf(pause) ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCatalog_GetRemarks_StatesWaitCeiling()
    {
        var method = Assert.Single(ToolMethods(), m => ToolName(m) == "figma_get_remarks");
        var wait = Assert.Single(method.GetParameters(), parameter => parameter.Name == "waitMs");

        Assert.Contains(
            ToolLimits.MaxRemarkWaitMs.ToString(),
            DescriptionOf(wait) ?? string.Empty,
            StringComparison.Ordinal);

        Assert.Contains(
            ToolLimits.MaxRemarkWaitMs.ToString(),
            DescriptionOf(method) ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCatalog_GetFileForPlugin_StatesDepthLimit()
    {
        var method = Assert.Single(ToolMethods(), m => ToolName(m) == "figma_get_file_for_plugin");
        var depth = Assert.Single(method.GetParameters(), parameter => parameter.Name == "depth");

        Assert.Contains(
            ToolLimits.MaxPluginViewDepth.ToString(),
            DescriptionOf(depth) ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>A host that loads tools on demand or brokers them from a sandbox decides by these hints whether a call needs a human; a silent hint is read as "unknown" and costs a prompt.</summary>
    [Fact]
    public void ToolCatalog_EveryTool_StatesAllFourBehaviourHints()
    {
        var silent = ToolMethods()
            .Where(method => Protocol(method).Annotations is not
            {
                ReadOnlyHint: not null,
                DestructiveHint: not null,
                IdempotentHint: not null,
                OpenWorldHint: not null,
            })
            .Select(ToolName)
            .ToList();

        Assert.True(silent.Count == 0, $"tools without explicit ReadOnly, Destructive, Idempotent and OpenWorld: {string.Join(", ", silent)}");
    }

    [Fact]
    public void ToolCatalog_ReadOnlyTool_IsNeverDestructive()
    {
        var contradictions = ToolMethods()
            .Where(method => Protocol(method).Annotations is { ReadOnlyHint: true, DestructiveHint: true })
            .Select(ToolName)
            .ToList();

        Assert.True(contradictions.Count == 0, $"read-only yet destructive: {string.Join(", ", contradictions)}");
    }

    [Fact]
    public void ToolCatalog_ReadOrDeleteByName_HintsMatchTheName()
    {
        var reads = ToolMethods()
            .Where(method => ReadingNamePattern.IsMatch(ToolName(method)))
            .Where(method => Protocol(method).Annotations is not { ReadOnlyHint: true })
            .Select(ToolName)
            .ToList();

        var deletes = ToolMethods()
            .Where(method => ToolName(method).Contains("_delete_", StringComparison.Ordinal))
            .Where(method => Protocol(method).Annotations is not { ReadOnlyHint: false, DestructiveHint: true })
            .Select(ToolName)
            .ToList();

        Assert.True(reads.Count == 0, $"named as a read but not read-only: {string.Join(", ", reads)}");
        Assert.True(deletes.Count == 0, $"named as a delete but not destructive: {string.Join(", ", deletes)}");
    }

    /// <summary>An output schema is a promise a code-mode host generates a typed stub from; a tool that states one must answer structured content, which only a CallToolResult can carry.</summary>
    [Fact]
    public void ToolCatalog_ToolWithOutputSchema_AnswersWithCallToolResult()
    {
        var advertised = ToolMethods().Where(method => Protocol(method).OutputSchema is not null).ToList();

        Assert.NotEmpty(advertised);
        Assert.All(advertised, method => Assert.Equal(typeof(Task<CallToolResult>), method.ReturnType));
    }

    [GeneratedRegex("^(figma|figjam|csharp)_[a-z0-9_]+$")]
    private static partial Regex ToolNamePattern { get; }

    [GeneratedRegex(@"\b(figma|figjam|csharp)_[a-z0-9_]+")]
    private static partial Regex ToolReference { get; }

    [GeneratedRegex("^(figma|figjam)_(get|list|search)_")]
    private static partial Regex ReadingNamePattern { get; }
}
