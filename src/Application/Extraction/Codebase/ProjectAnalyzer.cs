using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Settings of a full analysis run.</summary>
public sealed record AnalyzeOptions
{
    /// <summary>The directory the design-system package will be generated into; manifests go into its <c>.extraction/</c> folder.</summary>
    public required string OutDir { get; init; }

    /// <summary>Relative-path substrings; matching any keeps the file in the walk.</summary>
    public IReadOnlyList<string>? Include { get; init; }

    /// <summary>Relative-path substrings; matching any drops the file from the walk.</summary>
    public IReadOnlyList<string>? Exclude { get; init; }

    /// <summary>Number of files after which the walk of each target stops collecting.</summary>
    public int MaxFiles { get; init; } = 5000;
}

/// <summary>Manifest storage on disk under <c>&lt;outDir&gt;/.extraction/</c>; tools return a summary and the path.</summary>
public static class ExtractionManifests
{
    /// <summary>File name of the analysis manifest, see <see cref="DsAnalysis"/>.</summary>
    public const string AnalysisFile = "analysis.json";
    /// <summary>File name of the porting-progress manifest, see <see cref="DsStatusManifest"/>.</summary>
    public const string StatusFile = "status.json";
    /// <summary>File name of the token extraction report, see <see cref="DsTokensReport"/>.</summary>
    public const string TokensReportFile = "tokens-report.json";
    /// <summary>File name of the list of theme modes, written only when the tokens have more than one.</summary>
    public const string TokenModesFile = "tokens-modes.json";

    /// <summary>The <c>.extraction/</c> folder under <c>outDir</c> that holds every manifest.</summary>
    public static string Directory(string outDir) => Path.Combine(outDir, ".extraction");

    /// <summary>Full path of the analysis manifest for <c>outDir</c>.</summary>
    public static string AnalysisPath(string outDir) => Path.Combine(Directory(outDir), AnalysisFile);

    /// <summary>Full path of the porting-progress manifest for <c>outDir</c>.</summary>
    public static string StatusPath(string outDir) => Path.Combine(Directory(outDir), StatusFile);

    /// <summary>Relative paths resolve against the process working directory, like CLI tools do.</summary>
    public static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(System.IO.Directory.GetCurrentDirectory(), path));

    /// <summary>Writes beside the manifest and moves the file over it, so a reader meets the old manifest or the new one and never half of either.</summary>
    /// <remarks>A torn status.json reads as corrupt, a corrupt one as missing, and a missing one is started afresh: every component's progress went back to pending. The name beside it is unique because two server processes may write the same manifest.</remarks>
    public static void Write<T>(string path, T data)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        var written = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(written, JsonSerializer.Serialize(data, DsJson.Info<T>()) + Environment.NewLine);
        File.Move(written, path, overwrite: true);
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
            return JsonSerializer.Deserialize(raw, DsJson.Info<T>());
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

    /// <summary>The analysis of every target root: its stack, style sources, icons, typography, component inventory and architecture.</summary>
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
