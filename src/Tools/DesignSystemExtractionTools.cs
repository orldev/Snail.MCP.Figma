using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Extraction.Codebase;
using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Parsers;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Design-system extraction from a codebase (<c>figma_ds_*</c>): analyze → extract tokens → scaffold → extract components → status → verify; Figma joins only at token import.</summary>
[McpServerToolType]
public sealed partial class DesignSystemExtractionTools(ILogger<DesignSystemExtractionTools> logger)
{
    /// <summary>Analyzes one or more application codebases and writes the component and styling inventory that extraction starts from.</summary>
    [McpServerTool(Name = "figma_ds_analyze", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsAnalyze)]
    public Task<string> AnalyzeAsync(
        [Description("Application roots, comma-separated or as a JSON array. Absolute paths are safer: the server working directory is not your project.")] string targets,
        [Description("Where the design system package will be generated. Defaults to '<first target>/design-system'.")] string? outDir = null,
        [Description("Substrings of relative paths: when set, only matching files are scanned.")] string? include = null,
        [Description("Substrings of relative paths to skip. node_modules, dist, .next and the like are always skipped.")] string? exclude = null,
        [Description("Cap on the number of files per target.")] int maxFiles = 5000,
        CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var roots = ParseList(targets).Select(ExtractionManifests.ResolvePath).ToList();

            if (roots.Count == 0)
            {
                return ToolResponse.Error(
                    "No target was passed.",
                    "targets is application roots, comma-separated or as a JSON array of absolute paths.");
            }

            foreach (var root in roots.Where(root => !Directory.Exists(root)))
            {
                return ToolResponse.Error(
                    $"The target directory was not found: {root}",
                    "Pass absolute paths: the MCP server working directory usually differs from the project directory.");
            }

            var resolvedOut = ResolveOutDir(outDir, roots);

            var analysis = ProjectAnalyzer.Analyze(roots, new AnalyzeOptions
            {
                OutDir = resolvedOut,
                Include = ParseList(include),
                Exclude = ParseList(exclude),
                MaxFiles = Math.Clamp(maxFiles, 100, 20000),
            });

            var manifestPath = ExtractionManifests.AnalysisPath(resolvedOut);
            ExtractionManifests.Write(manifestPath, analysis);

            var statusPath = ExtractionManifests.StatusPath(resolvedOut);

            if (!File.Exists(statusPath))
            {
                ExtractionManifests.Write(statusPath, ProjectAnalyzer.InitStatus(analysis));
            }

            var portable = analysis.Components
                .Where(c => !string.Equals(c.Classification, DsClassifications.PureVendor, StringComparison.Ordinal))
                .ToList();

            var byClassification = new JsonObject();

            foreach (var group in analysis.Components.GroupBy(c => c.Classification, StringComparer.Ordinal))
            {
                byClassification[group.Key] = group.Count();
            }

            return ToolResponse.Ok(new JsonObject
            {
                ["manifestPath"] = manifestPath,
                ["outDir"] = resolvedOut,
                ["targets"] = new JsonArray(analysis.Targets.Select(TargetSummary).ToArray()),
                ["inventory"] = new JsonObject
                {
                    ["totalComponents"] = analysis.Components.Count,
                    ["portable"] = portable.Count,
                    ["pureVendor"] = analysis.Components.Count - portable.Count,
                    ["byClassification"] = byClassification,
                    ["topByUsage"] = new JsonArray(analysis.Components.Take(25).Select(c => (JsonNode)new JsonObject
                    {
                        ["name"] = c.Name,
                        ["file"] = c.File,
                        ["classification"] = c.Classification,
                        ["usage"] = c.Usage.TagCount + c.Usage.ImportCount,
                        ["props"] = c.Props.Count,
                    }).ToArray()),
                    ["duplicateGroups"] = Serialize(analysis.DuplicateGroups.Take(20)),
                },
                ["architecture"] = analysis.Architecture is null ? null : new JsonObject
                {
                    ["summary"] = Serialize(analysis.Architecture.Summary),
                    ["specializations"] = Serialize(analysis.Architecture.Specializations.Take(20)),
                    ["missingPrimitives"] = ToJsonArray(analysis.Architecture.MissingPrimitives),
                    ["guidance"] =
                        "Specialisations are an APPLICATION named as a component (FollowButton is a Button you follow with): " +
                        "a sign of a widget set, not of a system. For a general-purpose system: build every missingPrimitive " +
                        "as a generic component, express specialisations as variants or recipes on top of it, and settle the " +
                        "atomic design levels with the team.",
                },
                ["nextSteps"] = new JsonArray(
                    "Go through topByUsage, duplicateGroups and architecture.specializations with the team: what becomes a generic primitive and what becomes a recipe.",
                    "Run figma_ds_extract_tokens — it mines the styling decisions into DTCG tokens.",
                    "Next, figma_ds_scaffold assembles the design system package."),
            });
        }, cancellationToken);

    /// <summary>Mines design tokens from an analyzed codebase into DTCG and optional code formats.</summary>
    [McpServerTool(Name = "figma_ds_extract_tokens", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsExtractTokens)]
    public Task<string> ExtractTokensAsync(
        [Description("The directory used in figma_ds_analyze: .extraction/analysis.json is read from it.")] string? outDir = null,
        [Description("Scan these roots instead of the targets from the manifest. Rarely needed.")] string? targets = null,
        [Description("Comma-separated file formats under <outDir>/tokens/. dtcg is always written.")] string formats = "dtcg,css-vars",
        [Description("How many times a raw value must occur to become a token.")] int minFrequency = 4,
        [Description("Write files to disk. false is a dry run and returns the document in the response.")] bool write = true,
        CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            List<string> roots;
            string resolvedOut;

            var explicitTargets = ParseList(targets);

            if (explicitTargets.Count > 0)
            {
                roots = [.. explicitTargets.Select(ExtractionManifests.ResolvePath)];
                resolvedOut = ResolveOutDir(outDir, roots);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(outDir))
                {
                    return ToolResponse.Error(
                        "Pass outDir (the same one as in figma_ds_analyze) or explicit targets.",
                        "figma_ds_extract_tokens reads the manifest from <outDir>/.extraction/analysis.json.");
                }

                resolvedOut = ExtractionManifests.ResolvePath(outDir);
                var analysis = ExtractionManifests.Read<DsAnalysis>(ExtractionManifests.AnalysisPath(resolvedOut));

                if (analysis is null)
                {
                    return ToolResponse.Error(
                        $"There is no analysis manifest: {ExtractionManifests.AnalysisPath(resolvedOut)}.",
                        "Run figma_ds_analyze with the same outDir first.");
                }

                roots = [.. analysis.Targets.Select(t => t.Root)];
            }

            var files = roots
                .SelectMany(root => CodeWalker.Walk(root, new WalkOptions
                {
                    Extensions =
                    [
                        ".css", ".scss", ".sass", ".less", ".tsx", ".jsx", ".ts", ".js",
                        ".cjs", ".mjs", ".astro", ".vue", ".svelte",
                    ],
                }).Files)
                .ToList();

            var extraction = CodeTokenExtractor.Extract(files, new ExtractTokensOptions
            {
                MinFrequency = Math.Clamp(minFrequency, 2, 100),
                TargetRoots = roots,
            });

            var report = new DsTokensReport
            {
                Counts = extraction.Counts,
                Warnings = extraction.Warnings,
                BelowThreshold = extraction.BelowThreshold,
            };

            if (write)
            {
                var written = TokenFileWriter.Write(
                    extraction.Document,
                    resolvedOut,
                    TokenFileWriter.WithCanonicalFirst(ParseList(formats)));

                report.FilesWritten.AddRange(written.FilesWritten);
                report.Warnings.AddRange(written.Warnings);

                ExtractionManifests.Write(
                    Path.Combine(ExtractionManifests.Directory(resolvedOut), ExtractionManifests.TokensReportFile),
                    report);

                var modes = TokenFileWriter.ModeNames(extraction.Document);

                if (modes.Count > 1)
                {
                    ExtractionManifests.Write(
                        Path.Combine(ExtractionManifests.Directory(resolvedOut), ExtractionManifests.TokenModesFile),
                        modes);
                }
            }

            return ToolResponse.Ok(new JsonObject
            {
                ["outDir"] = resolvedOut,
                ["counts"] = Serialize(extraction.Counts),
                ["sets"] = new JsonArray(extraction.Document.Sets.Select(set => (JsonNode)new JsonObject
                {
                    ["name"] = set.Name,
                    ["modes"] = ToJsonArray(set.Modes),
                    ["tokens"] = set.Tokens.Count,
                }).ToArray()),
                ["warnings"] = ToJsonArray(report.Warnings),
                ["belowThresholdSample"] = Serialize(extraction.BelowThreshold.Take(15)),
                ["filesWritten"] = ToJsonArray(report.FilesWritten),
                ["document"] = write ? null : RenderDtcg(extraction.Document),
                ["nextSteps"] = new JsonArray(
                    "Do a semantic naming pass with the team: structural names are lifted into role names by alias tokens.",
                    "Next figma_ds_scaffold; if design leads, figma_import_tokens over tokens/tokens.json creates the Figma variables."),
            });
        }, cancellationToken);

    /// <summary>Generates the skeleton of the design system package in the output folder from the analysis and the tokens.</summary>
    [McpServerTool(Name = "figma_ds_scaffold", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsScaffold)]
    public Task<string> ScaffoldAsync(
        [Description("The directory used in figma_ds_analyze and figma_ds_extract_tokens.")] string outDir,
        [Description("npm package name of the design system, for example '@acme/design-system'.")] string? packageName = null,
        [Description("Scaffold framework. Defaults to the first one the analysis found. Storybook has no .astro renderer: for astro the workshop is built on react-vite.")] string? framework = null,
        [Description("Comma-separated token file formats.")] string formats = "dtcg,css-vars",
        [Description("Overwrite scaffold files that already exist.")] bool force = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var resolvedOut = ExtractionManifests.ResolvePath(outDir);
            var analysis = ExtractionManifests.Read<DsAnalysis>(ExtractionManifests.AnalysisPath(resolvedOut));

            if (analysis is null)
            {
                return ToolResponse.Error(
                    $"There is no analysis manifest: {ExtractionManifests.AnalysisPath(resolvedOut)}.",
                    "Run figma_ds_analyze with the same outDir first.");
            }

            var tokensPath = Path.Combine(resolvedOut, "tokens", "tokens.json");

            if (!File.Exists(tokensPath))
            {
                return ToolResponse.Error(
                    $"There are no mined tokens: {tokensPath}.",
                    "Run figma_ds_extract_tokens with the same outDir first.");
            }

            var document = new DtcgParser()
                .Parse(File.ReadAllText(tokensPath), new ParseContext("Tokens", new TokenDocument()))
                .Document;

            var resolvedFramework = framework
                ?? analysis.Targets.FirstOrDefault()?.Frameworks.FirstOrDefault()
                ?? DsFrameworks.React;

            var result = DesignSystemScaffolder.Scaffold(analysis, document, new ScaffoldOptions
            {
                OutDir = resolvedOut,
                PackageName = packageName ?? "@extracted/design-system",
                Framework = resolvedFramework,
                Formats = ParseList(formats),
                ShouldOverwrite = force,
            });

            return ToolResponse.Ok(new JsonObject
            {
                ["outDir"] = resolvedOut,
                ["framework"] = resolvedFramework,
                ["filesWritten"] = ToJsonArray(result.FilesWritten),
                ["filesSkipped"] = ToJsonArray(result.FilesSkipped),
                ["warnings"] = ToJsonArray(result.Warnings),
                ["nextSteps"] = ToJsonArray(result.NextSteps),
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_ds_setup_storybook", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsSetupStorybook)]
    public Task<string> SetupStorybookAsync(
        [Description("Directory of the design system package with a freshly installed .storybook.")] string outDir,
        CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var resolvedOut = ExtractionManifests.ResolvePath(outDir);
            var analysis = ExtractionManifests.Read<DsAnalysis>(ExtractionManifests.AnalysisPath(resolvedOut));

            if (analysis is null)
            {
                return ToolResponse.Error(
                    $"There is no analysis manifest: {ExtractionManifests.AnalysisPath(resolvedOut)}.",
                    "Run figma_ds_analyze with the same outDir first.");
            }

            var result = StorybookPreset.Setup(analysis, resolvedOut);

            return ToolResponse.Ok(new JsonObject
            {
                ["filesWritten"] = ToJsonArray(result.FilesWritten),
                ["patched"] = ToJsonArray(result.Patched),
                ["warnings"] = ToJsonArray(result.Warnings),
                ["nextSteps"] = ToJsonArray(result.ManualSteps.Append(
                    "Restart the Storybook dev server if it was running: changes in @import CSS are not always picked up.")),
            });
        }, cancellationToken);

    /// <summary>Breaks one inventory component down for porting, with its sources, props, variants and a story scaffold.</summary>
    [McpServerTool(Name = "figma_ds_extract_component", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsExtractComponent)]
    public Task<string> ExtractComponentAsync(
        [Description("The directory used in figma_ds_analyze.")] string outDir,
        [Description("Component name from the inventory: see topByUsage in the figma_ds_analyze response or the manifest itself.")] string component,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(ComponentPorting.Extract(outDir, component)), cancellationToken);

    /// <summary>Runs the deterministic checks on the extracted package before handover or import into Figma.</summary>
    [McpServerTool(Name = "figma_ds_verify", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsVerify)]
    public Task<string> VerifyAsync(
        [Description("Design system package directory.")] string outDir,
        CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var resolvedOut = ExtractionManifests.ResolvePath(outDir);
            var analysis = ExtractionManifests.Read<DsAnalysis>(ExtractionManifests.AnalysisPath(resolvedOut));
            var status = ExtractionManifests.Read<DsStatusManifest>(ExtractionManifests.StatusPath(resolvedOut));

            var report = ExtractionVerifier.Verify(resolvedOut, analysis, status);

            return ToolResponse.Ok(new JsonObject
            {
                ["outDir"] = resolvedOut,
                ["passed"] = report.HasPassed,
                ["checks"] = Serialize(report.Checks),
                ["figmaRoundTrip"] = new JsonObject
                {
                    ["ready"] = report.IsFigmaRoundTripReady,
                    ["how"] = ToJsonArray(report.FigmaRoundTripHow),
                },
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_ds_status", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsStatus)]
    public Task<string> StatusAsync(
        [Description("The directory used in figma_ds_analyze.")] string outDir,
        [Description("Component name from the inventory. Empty just reads the progress.")] string? component = null,
        [Description("New status: pending, in-progress, ported, skipped.")] string? status = null,
        [Description("Why it was skipped, what was merged, what to watch out for.")] string? notes = null,
        [Description("Relative path of the story file once it is written.")] string? storyFile = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(PortingProgress.Update(outDir, component, status, notes, storyFile)), cancellationToken);

    private static string Answer(ExtractionAnswer answer) =>
        answer.Payload is { } payload ? ToolResponse.Ok(payload) : ToolResponse.Error(answer.Error!, answer.Hint);

    /// <summary>File-system errors come back as data so the model can read the cause and fix the call.</summary>
    private Task<string> RunAsync(Func<string> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(action());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ExtractionFileSystemError(logger, ex);

            return Task.FromResult(ToolResponse.Error(
                ex.Message,
                "Check the directory permissions and that the path points at an existing project."));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException or RegexMatchTimeoutException)
        {
            Log.ExtractionFailed(logger, ex);

            return Task.FromResult(ToolResponse.Error(ex.Message));
        }
    }

    /// <summary>Default output directory — <c>design-system</c> inside the first target.</summary>
    private static string ResolveOutDir(string? outDir, IReadOnlyList<string> roots) =>
        string.IsNullOrWhiteSpace(outDir)
            ? Path.Combine(roots[0], "design-system")
            : ExtractionManifests.ResolvePath(outDir);

    /// <summary>List from a JSON array or a comma/newline separated string.</summary>
    private static List<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var trimmed = value.Trim();

        if (trimmed.StartsWith('['))
        {
            try
            {
                if (JsonNode.Parse(trimmed) is JsonArray array)
                {
                    return [.. array
                        .Select(item => item?.GetValue<string>())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!.Trim())];
                }
            }
            catch (JsonException)
            {
            }
        }

        return [.. trimmed.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        [.. values.Select(value => (JsonNode)JsonValue.Create(value)!)];

    /// <summary>One analysed target as figma_ds_analyze reports it.</summary>
    private static JsonNode TargetSummary(DsTargetAnalysis target) => new JsonObject
    {
        ["root"] = target.Root,
        ["packageName"] = target.PackageName,
        ["frameworks"] = ToJsonArray(target.Frameworks),
        ["stylingMethods"] = ToJsonArray(target.StylingMethods),
        ["componentLibraries"] = ToJsonArray(target.ComponentLibraries.Select(l => l.Name)),
        ["icons"] = target.Icons.Count,
        ["typographyRules"] = target.Typography.Count,
        ["stats"] = Serialize(target.Stats),
    };

    private static JsonNode? Serialize<T>(T value) => JsonSerializer.SerializeToNode(value, DsJson.Info<T>());

    /// <summary>Token document as canonical DTCG — for dry runs when no files are written.</summary>
    private static JsonNode? RenderDtcg(TokenDocument document)
    {
        var formatter = Application.Tokens.Formatters.TokenFormatters.Find("dtcg")!;
        var file = formatter.Write(document, new Application.Tokens.Formatters.FormatOptions()).Files.FirstOrDefault();

        return file is null ? null : JsonNode.Parse(file.Content);
    }
}
