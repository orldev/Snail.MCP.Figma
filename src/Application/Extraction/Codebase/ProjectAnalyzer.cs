using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Settings of a full analysis run.</summary>
public sealed record AnalyzeOptions
{
    public required string OutDir { get; init; }

    public IReadOnlyList<string>? Include { get; init; }

    public IReadOnlyList<string>? Exclude { get; init; }

    public int MaxFiles { get; init; } = 5000;
}

/// <summary>Manifest storage on disk under <c>&lt;outDir&gt;/.extraction/</c>; tools return a summary and the path.</summary>
public static class ExtractionManifests
{
    public const string AnalysisFile = "analysis.json";
    public const string StatusFile = "status.json";
    public const string TokensReportFile = "tokens-report.json";
    public const string TokenModesFile = "tokens-modes.json";

    public static string Directory(string outDir) => Path.Combine(outDir, ".extraction");

    public static string AnalysisPath(string outDir) => Path.Combine(Directory(outDir), AnalysisFile);

    public static string StatusPath(string outDir) => Path.Combine(Directory(outDir), StatusFile);

    /// <summary>Relative paths resolve against the process working directory, like CLI tools do.</summary>
    public static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(System.IO.Directory.GetCurrentDirectory(), path));

    public static void Write<T>(string path, T data)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(data, DsJson.Options) + Environment.NewLine);
    }

    /// <summary>Manifest from disk; <c>null</c> when missing or corrupt, so the call does not fail.</summary>
    public static T? Read<T>(string path) where T : class
    {
        var raw = CodeWalker.ReadText(path);

        if (raw is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, DsJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Full analysis run: walk each target, detect the stack, build the component inventory and classify the architecture.</summary>
public static partial class ProjectAnalyzer
{
    [GeneratedRegex(@"tailwind\.config\.(js|cjs|mjs|ts)$")]
    private static partial Regex TailwindConfigPattern();

    [GeneratedRegex(@"\.module\.(css|scss)$")]
    private static partial Regex CssModulePattern();

    [GeneratedRegex(@"--[\w-]+\s*:")]
    private static partial Regex CustomPropertyProbe();

    private const int MaxIcons = 200;
    private const int MaxStyleSources = 200;
    private const int MaxTypographyRules = 120;
    private const long MaxIconBytes = 32 * 1024;

    public static DsAnalysis Analyze(IReadOnlyList<string> targetRoots, AnalyzeOptions options)
    {
        var analysis = new DsAnalysis { OutDir = options.OutDir };

        for (var targetIndex = 0; targetIndex < targetRoots.Count; targetIndex++)
        {
            var root = targetRoots[targetIndex];
            var started = Stopwatch.StartNew();

            var walk = CodeWalker.Walk(root, new WalkOptions
            {
                Include = options.Include,
                Exclude = options.Exclude,
                MaxFiles = options.MaxFiles,
            });

            var package = StackDetectors.ReadPackageInfo(root);
            var frameworks = StackDetectors.DetectFrameworks(package, walk.Files);
            var stylingMethods = StackDetectors.DetectStylingMethods(package, walk.Files);
            var componentLibraries = StackDetectors.DetectComponentLibraries(root, package);

            var styleSources = new List<DsStyleSource>();
            var icons = new List<DsIconEntry>();
            var typography = new List<DsTypographyRule>();

            foreach (var file in walk.Files)
            {
                CollectStyleAssets(file, styleSources, icons, typography);
            }

            var inventory = ComponentInventory.Build(walk.Files, targetIndex);

            analysis.Targets.Add(new DsTargetAnalysis
            {
                Root = root,
                PackageName = package.Name,
                Frameworks = frameworks,
                StylingMethods = stylingMethods,
                ComponentLibraries = componentLibraries,
                StyleSources = [.. styleSources.Take(MaxStyleSources)],
                Icons = icons,
                Typography = [.. typography.Take(MaxTypographyRules)],
                Stats = new DsTargetStats
                {
                    FilesScanned = walk.Files.Count,
                    FilesSkipped = walk.Skipped.Ignored + walk.Skipped.TooLarge,
                    DurationMs = started.ElapsedMilliseconds,
                },
            });

            analysis.Components.AddRange(inventory.Components);
            analysis.DuplicateGroups.AddRange(inventory.DuplicateGroups);
        }

        if (targetRoots.Count > 1)
        {
            var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var component in analysis.Components)
            {
                if (component.File is null)
                {
                    continue;
                }

                if (!byName.TryGetValue(component.Name, out var paths))
                {
                    paths = [];
                    byName[component.Name] = paths;
                }

                paths.Add($"{targetRoots[component.TargetIndex]}/{component.File}");
            }

            foreach (var (name, files) in byName)
            {
                if (files.Count > 1 && !analysis.DuplicateGroups.Any(g => string.Equals(g.Name, name, StringComparison.Ordinal)))
                {
                    analysis.DuplicateGroups.Add(new DsDuplicateGroup { Name = name, Files = files });
                }
            }
        }

        analysis.Architecture = ArchitectureClassifier.Classify(analysis.Components);

        return analysis;
    }

    /// <summary>Initial progress manifest; pure-vendor entries have nothing to port.</summary>
    public static DsStatusManifest InitStatus(DsAnalysis analysis)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var status = new DsStatusManifest { UpdatedAt = now };

        foreach (var component in analysis.Components)
        {
            if (string.Equals(component.Classification, DsClassifications.PureVendor, StringComparison.Ordinal))
            {
                continue;
            }

            status.Components[component.Name] = new DsStatusEntry
            {
                Status = DsPortStatuses.Pending,
                UpdatedAt = now,
            };
        }

        return status;
    }

    private static void CollectStyleAssets(
        WalkedFile file,
        List<DsStyleSource> styleSources,
        List<DsIconEntry> icons,
        List<DsTypographyRule> typography)
    {
        if (TailwindConfigPattern().IsMatch(file.RelativePath))
        {
            styleSources.Add(new DsStyleSource { File = file.RelativePath, Kind = "tailwind-config" });

            return;
        }

        if (CssModulePattern().IsMatch(file.RelativePath))
        {
            styleSources.Add(new DsStyleSource { File = file.RelativePath, Kind = "css-module" });

            return;
        }

        if (file.Extension is ".scss" or ".sass")
        {
            styleSources.Add(new DsStyleSource { File = file.RelativePath, Kind = "scss" });

            if (CodeWalker.ReadText(file.Path) is { } scss)
            {
                typography.AddRange(Convert(CodeStyleMiner.MineTypographyStyles(scss, file.RelativePath)));
            }

            return;
        }

        if (file.Extension == ".css")
        {
            var text = CodeWalker.ReadText(file.Path);
            var variableCount = text is null ? 0 : CustomPropertyProbe().Matches(text).Count;

            styleSources.Add(new DsStyleSource
            {
                File = file.RelativePath,
                Kind = "css",
                Note = variableCount > 0 ? $"declares {variableCount} custom properties" : null,
            });

            if (text is not null)
            {
                typography.AddRange(Convert(CodeStyleMiner.MineTypographyStyles(text, file.RelativePath)));
            }

            return;
        }

        if (file.Extension == ".svg" && file.SizeBytes <= MaxIconBytes)
        {
            if (icons.Count < MaxIcons)
            {
                icons.Add(new DsIconEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file.RelativePath),
                    File = file.RelativePath,
                    Source = "svg-file",
                });
            }

            return;
        }

        if (file.Extension is not (".astro" or ".tsx" or ".jsx") || icons.Count >= MaxIcons)
        {
            return;
        }

        var source = CodeWalker.ReadText(file.Path);

        if (source is null)
        {
            return;
        }

        if (source.Contains("<svg", StringComparison.Ordinal))
        {
            icons.Add(new DsIconEntry
            {
                Name = Path.GetFileNameWithoutExtension(file.RelativePath),
                File = file.RelativePath,
                Source = "inline-svg",
            });
        }

        if (file.Extension == ".astro")
        {
            foreach (var block in CodeStyleMiner.ExtractStyleBlocks(source))
            {
                typography.AddRange(Convert(CodeStyleMiner.MineTypographyStyles(block, file.RelativePath)));
            }
        }
    }

    private static IEnumerable<DsTypographyRule> Convert(IEnumerable<MinedTypographyRule> rules) =>
        rules.Select(rule => new DsTypographyRule
        {
            Selector = rule.Selector,
            Properties = rule.Properties,
            Source = rule.Source,
        });
}
