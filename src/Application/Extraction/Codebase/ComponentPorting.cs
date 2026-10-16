using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>An answer of the extraction: the payload, or the failure and the way out — a failure the caller can act on, carried as data.</summary>
public sealed record ExtractionAnswer(JsonObject? Payload, string? Error = null, string? Hint = null)
{
    /// <summary>A successful answer carrying the payload.</summary>
    public static ExtractionAnswer Ok(JsonObject payload) => new(payload);

    /// <summary>A failed answer carrying no payload, only the error and the hint that leads out of it.</summary>
    public static ExtractionAnswer Fail(string error, string hint) => new(null, error, hint);
}

/// <summary>One component of the inventory made ready to port: its source, what it imports and which styles it touches, a story to start from.</summary>
public sealed partial class ComponentPorting(CodeWalker sources, ExtractionManifests manifests)
{
    [GeneratedRegex(@"import\s+[^'""]*from\s+['""](\.[^'""]+)['""]")]
    private static partial Regex LocalImportPattern();

    [GeneratedRegex(@"\.(module\.)?(css|scss)$")]
    private static partial Regex StyleImportPattern();

    [GeneratedRegex(@"class(?:Name)?\s*=\s*(?:""([^""]+)""|'([^']+)')")]
    private static partial Regex ClassAttributePattern();

    [GeneratedRegex(@"var\(\s*(--[\w-]+)")]
    private static partial Regex CustomPropertyUsagePattern();

    private const int MaxComponentSourceKb = 64;

    /// <summary>The component named in the analysis manifest under <paramref name="outDir"/>, read from its own app.</summary>
    public ExtractionAnswer Extract(string outDir, string component)
    {
        var resolvedOut = ExtractionManifests.ResolvePath(outDir);
        var analysis = manifests.Read<DsAnalysis>(ExtractionManifests.AnalysisPath(resolvedOut));

        if (analysis is null)
        {
            return ExtractionAnswer.Fail(
                $"There is no analysis manifest: {ExtractionManifests.AnalysisPath(resolvedOut)}.",
                "Run figma_ds_analyze with the same outDir first.");
        }

        var matches = analysis.Components
            .Where(c => string.Equals(c.Name, component, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            var near = analysis.Components
                .Where(c => c.Name.Contains(component, StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .Select(c => c.Name)
                .ToList();

            return ExtractionAnswer.Fail(
                $"Component '{component}' was not found in the inventory.",
                near.Count > 0 ? $"Did you mean one of: {string.Join(", ", near)}?" : "See the available names in the figma_ds_analyze response.");
        }

        var entry = matches.FirstOrDefault(c =>
            !string.Equals(c.Classification, DsClassifications.PureVendor, StringComparison.Ordinal)) ?? matches[0];

        if (string.Equals(entry.Classification, DsClassifications.PureVendor, StringComparison.Ordinal) || entry.File is null)
        {
            return ExtractionAnswer.Ok(new JsonObject
            {
                ["component"] = Serialize(entry),
                ["portable"] = false,
                ["guidance"] =
                    "A pure vendor component — there is nothing to port. Describe it through the token theme and " +
                    "agreed-usage documentation; if you need your own version, add a wrapper in the design system.",
            });
        }

        var root = analysis.Targets.ElementAtOrDefault(entry.TargetIndex)?.Root;

        if (root is null)
        {
            return ExtractionAnswer.Fail(
                $"Component {entry.Name} has no target root in the manifest.",
                "The manifest is stale — rerun figma_ds_analyze.");
        }

        var absolute = sources.ResolveUnder(root, entry.File);

        if (absolute is null)
        {
            return ExtractionAnswer.Fail(
                $"Component {entry.Name} points outside its target root: {entry.File}.",
                "The analysis manifest was not written by figma_ds_analyze as it stands — rerun figma_ds_analyze.");
        }

        var source = sources.ReadText(absolute, MaxComponentSourceKb * 1024);

        if (source is null)
        {
            return ExtractionAnswer.Fail(
                $"File {absolute} could not be read.",
                "The analysis manifest may be stale — rerun figma_ds_analyze.");
        }

        var truncated = source.Length > MaxComponentSourceKb * 1024;

        if (truncated)
        {
            source = source[..(MaxComponentSourceKb * 1024)];
        }

        var localImports = new List<string>();
        var styleImports = new List<string>();
        var directory = Path.GetDirectoryName(absolute)!;

        foreach (Match match in LocalImportPattern().Matches(source))
        {
            var specifier = match.Groups[1].Value;

            if (StyleImportPattern().IsMatch(specifier))
            {
                styleImports.Add(specifier);
            }
            else
            {
                localImports.Add(Path.GetFullPath(Path.Combine(directory, specifier.Replace('/', Path.DirectorySeparatorChar))));
            }
        }

        var classNames = new List<string>();

        foreach (Match match in ClassAttributePattern().Matches(source))
        {
            var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

            foreach (var cls in raw.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (classNames.Count < 80 && !classNames.Contains(cls, StringComparer.Ordinal))
                {
                    classNames.Add(cls);
                }
            }
        }

        var customProperties = CustomPropertyUsagePattern().Matches(source)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return ExtractionAnswer.Ok(new JsonObject
        {
            ["component"] = new JsonObject
            {
                ["name"] = entry.Name,
                ["file"] = entry.File,
                ["sourceApp"] = root,
                ["classification"] = entry.Classification,
                ["vendorModule"] = entry.VendorModule,
                ["props"] = Serialize(entry.Props),
                ["observedProps"] = Serialize(entry.ObservedProps),
                ["usage"] = Serialize(entry.Usage),
                ["duplicates"] = Serialize(entry.Duplicates),
            },
            ["source"] = source,
            ["sourceTruncated"] = truncated,
            ["localImports"] = ToJsonArray(localImports.Take(30)),
            ["styleTouchpoints"] = new JsonObject
            {
                ["cssModuleImports"] = ToJsonArray(styleImports),
                ["classNames"] = ToJsonArray(classNames),
                ["customProperties"] = ToJsonArray(customProperties),
            },
            ["storyScaffold"] = DesignSystemScaffolder.StoryScaffold(entry),
            ["portingChecklist"] = new JsonArray(
                $"Create <outDir>/src/components/{entry.Name}/{entry.Name}.<ext> and detach the app specifics: routing, state, data loading.",
                "Replace hardcoded values with tokens from tokens/ — check styleTouchpoints.customProperties against tokens.json.",
                "Finish storyScaffold into <Name>.stories: one story per real variant.",
                "Check it by eye in Storybook and record it with figma_ds_status."),
        });
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        [.. values.Select(value => (JsonNode)JsonValue.Create(value)!)];

    private static JsonNode? Serialize<T>(T value) => JsonSerializer.SerializeToNode(value, DsJson.Info<T>());
}
