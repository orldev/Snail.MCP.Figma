using System.Text.Json;
using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Formatters;
using Snail.MCP.Figma.Application.Tokens.Parsers;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>The extraction of a design system from a codebase, step by step: analyse the apps, mine the tokens, scaffold the package, wire Storybook, port the components, verify, record progress.</summary>
/// <remarks>Every step answers with data the agent reads, including its failures; the tools over it only parse their arguments and pack the answer.</remarks>
public sealed class DesignSystemExtraction(
    IWorkspace workspace,
    CodeWalker sources,
    ProjectAnalyzer analyzer,
    ExtractionManifests manifests,
    CodeTokenExtractor tokens,
    TokenFileWriter tokenFiles,
    DesignSystemScaffolder scaffolder,
    StorybookPreset storybook,
    ExtractionVerifier verifier,
    ComponentPorting porting,
    PortingProgress progress)
{
    /// <summary>The extensions the token mining reads.</summary>
    private static readonly string[] TokenSourceExtensions =
    [
        ".css", ".scss", ".sass", ".less", ".tsx", ".jsx", ".ts", ".js",
        ".cjs", ".mjs", ".astro", ".vue", ".svelte",
    ];

    /// <summary>Analyses the application roots and writes the analysis manifest, and the first progress manifest when there is none.</summary>
    public ExtractionAnswer Analyze(
        IReadOnlyList<string> targets,
        string? outDir,
        IReadOnlyList<string> include,
        IReadOnlyList<string> exclude,
        int maxFiles)
    {
        var roots = targets.Select(ExtractionManifests.ResolvePath).ToList();

        if (roots.Count == 0)
        {
            return ExtractionAnswer.Fail(
                "No target was passed.",
                "targets is application roots, comma-separated or as a JSON array of absolute paths.");
        }

        foreach (var root in roots.Where(root => !workspace.HasDirectory(root)))
        {
            return ExtractionAnswer.Fail(
                $"The target directory was not found: {root}",
                "Pass absolute paths: the MCP server working directory usually differs from the project directory.");
        }

        var resolvedOut = ResolveOutDir(outDir, roots);

        var analysis = analyzer.Analyze(roots, new AnalyzeOptions
        {
            OutDir = resolvedOut,
            Include = include,
            Exclude = exclude,
            MaxFiles = Math.Clamp(maxFiles, 100, 20000),
        });

        var manifestPath = ExtractionManifests.AnalysisPath(resolvedOut);
        manifests.Write(manifestPath, analysis);

        var statusPath = ExtractionManifests.StatusPath(resolvedOut);

        if (!workspace.HasFile(statusPath))
        {
            manifests.Write(statusPath, ProjectAnalyzer.InitStatus(analysis));
        }

        var portable = analysis.Components
            .Where(c => !string.Equals(c.Classification, DsClassifications.PureVendor, StringComparison.Ordinal))
            .ToList();

        var byClassification = new JsonObject();

        foreach (var group in analysis.Components.GroupBy(c => c.Classification, StringComparer.Ordinal))
        {
            byClassification[group.Key] = group.Count();
        }

        var topByUsage = analysis.Components.Take(25).Select(c => (JsonNode)new JsonObject
        {
            ["name"] = c.Name,
            ["file"] = c.File,
            ["classification"] = c.Classification,
            ["usage"] = c.Usage.TagCount + c.Usage.ImportCount,
            ["props"] = c.Props.Count,
        });

        var inventory = new JsonObject
        {
            ["totalComponents"] = analysis.Components.Count,
            ["portable"] = portable.Count,
            ["pureVendor"] = analysis.Components.Count - portable.Count,
            ["byClassification"] = byClassification,
            ["topByUsage"] = new JsonArray([.. topByUsage]),
            ["duplicateGroups"] = Serialize(analysis.DuplicateGroups.Take(20)),
        };

        var architecture = analysis.Architecture is null ? null : new JsonObject
        {
            ["summary"] = Serialize(analysis.Architecture.Summary),
            ["specializations"] = Serialize(analysis.Architecture.Specializations.Take(20)),
            ["missingPrimitives"] = ToJsonArray(analysis.Architecture.MissingPrimitives),
            ["guidance"] =
                "Specialisations are an APPLICATION named as a component (FollowButton is a Button you follow with): " +
                "a sign of a widget set, not of a system. For a general-purpose system: build every missingPrimitive " +
                "as a generic component, express specialisations as variants or recipes on top of it, and settle the " +
                "atomic design levels with the team.",
        };

        var answer = new JsonObject
        {
            ["manifestPath"] = manifestPath,
            ["outDir"] = resolvedOut,
            ["targets"] = new JsonArray([.. analysis.Targets.Select(TargetSummary)]),
            ["inventory"] = inventory,
            ["architecture"] = architecture,
            ["nextSteps"] = new JsonArray(
                "Go through topByUsage, duplicateGroups and architecture.specializations with the team: what becomes a generic primitive and what becomes a recipe.",
                "Run figma_ds_extract_tokens — it mines the styling decisions into DTCG tokens.",
                "Next, figma_ds_scaffold assembles the design system package."),
        };

        return ExtractionAnswer.Ok(answer);
    }

    /// <summary>Mines the tokens of the analysed apps, or of the roots named instead, and writes them under <c>tokens/</c> unless asked for a dry run.</summary>
    public ExtractionAnswer ExtractTokens(
        string? outDir,
        IReadOnlyList<string> targets,
        IReadOnlyList<string> formats,
        int minFrequency,
        bool shouldWrite)
    {
        List<string> roots;
        string resolvedOut;

        if (targets.Count > 0)
        {
            roots = [.. targets.Select(ExtractionManifests.ResolvePath)];
            resolvedOut = ResolveOutDir(outDir, roots);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(outDir))
            {
                return ExtractionAnswer.Fail(
                    "Pass outDir (the same one as in figma_ds_analyze) or explicit targets.",
                    "figma_ds_extract_tokens reads the manifest from <outDir>/.extraction/analysis.json.");
            }

            resolvedOut = ExtractionManifests.ResolvePath(outDir);

            if (ReadAnalysis(resolvedOut) is not { } analysis)
            {
                return NoAnalysis(resolvedOut);
            }

            roots = [.. analysis.Targets.Select(t => t.Root)];
        }

        var files = roots
            .SelectMany(root => sources.Walk(root, new WalkOptions { Extensions = TokenSourceExtensions }).Files)
            .ToList();

        var extraction = tokens.Extract(files, new ExtractTokensOptions
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

        if (shouldWrite)
        {
            WriteTokens(resolvedOut, formats, extraction, report);
        }

        var sets = extraction.Document.Sets.Select(set => (JsonNode)new JsonObject
        {
            ["name"] = set.Name,
            ["modes"] = ToJsonArray(set.Modes),
            ["tokens"] = set.Tokens.Count,
        });

        var answer = new JsonObject
        {
            ["outDir"] = resolvedOut,
            ["counts"] = Serialize(extraction.Counts),
            ["sets"] = new JsonArray([.. sets]),
            ["warnings"] = ToJsonArray(report.Warnings),
            ["belowThresholdSample"] = Serialize(extraction.BelowThreshold.Take(15)),
            ["filesWritten"] = ToJsonArray(report.FilesWritten),
            ["document"] = shouldWrite ? null : RenderDtcg(extraction.Document),
            ["nextSteps"] = new JsonArray(
                "Do a semantic naming pass with the team: structural names are lifted into role names by alias tokens.",
                "Next figma_ds_scaffold; if design leads, figma_import_tokens over tokens/tokens.json creates the Figma variables."),
        };

        return ExtractionAnswer.Ok(answer);
    }

    /// <summary>Writes the package skeleton — package.json, the token files, the showcase pages and a README — into the output directory.</summary>
    public ExtractionAnswer Scaffold(string outDir, string? packageName, string? framework, IReadOnlyList<string> formats, bool shouldOverwrite)
    {
        var resolvedOut = ExtractionManifests.ResolvePath(outDir);

        if (ReadAnalysis(resolvedOut) is not { } analysis)
        {
            return NoAnalysis(resolvedOut);
        }

        var tokensPath = Path.Combine(resolvedOut, "tokens", "tokens.json");

        if (workspace.ReadText(tokensPath) is not { } tokensJson)
        {
            return ExtractionAnswer.Fail(
                $"There are no mined tokens: {tokensPath}.",
                "Run figma_ds_extract_tokens with the same outDir first.");
        }

        var document = new DtcgParser()
            .Parse(tokensJson, new ParseContext("Tokens", new TokenDocument()))
            .Document;

        var resolvedFramework = framework
            ?? analysis.Targets.FirstOrDefault()?.Frameworks.FirstOrDefault()
            ?? DsFrameworks.React;

        var result = scaffolder.Scaffold(analysis, document, new ScaffoldOptions
        {
            OutDir = resolvedOut,
            PackageName = packageName ?? "@extracted/design-system",
            Framework = resolvedFramework,
            Formats = formats,
            ShouldOverwrite = shouldOverwrite,
        });

        var answer = new JsonObject
        {
            ["outDir"] = resolvedOut,
            ["framework"] = resolvedFramework,
            ["filesWritten"] = ToJsonArray(result.FilesWritten),
            ["filesSkipped"] = ToJsonArray(result.FilesSkipped),
            ["warnings"] = ToJsonArray(result.Warnings),
            ["nextSteps"] = ToJsonArray(result.NextSteps),
        };

        return ExtractionAnswer.Ok(answer);
    }

    /// <summary>Wires a freshly installed Storybook in the package to the tokens, fonts and themes.</summary>
    public ExtractionAnswer SetupStorybook(string outDir)
    {
        var resolvedOut = ExtractionManifests.ResolvePath(outDir);

        if (ReadAnalysis(resolvedOut) is not { } analysis)
        {
            return NoAnalysis(resolvedOut);
        }

        var result = storybook.Setup(analysis, resolvedOut);

        var answer = new JsonObject
        {
            ["filesWritten"] = ToJsonArray(result.FilesWritten),
            ["patched"] = ToJsonArray(result.Patched),
            ["warnings"] = ToJsonArray(result.Warnings),
            ["nextSteps"] = ToJsonArray(result.ManualSteps.Append(
                "Restart the Storybook dev server if it was running: changes in @import CSS are not always picked up.")),
        };

        return ExtractionAnswer.Ok(answer);
    }

    /// <summary>One inventory component made ready to port.</summary>
    public ExtractionAnswer ExtractComponent(string outDir, string component) => porting.Extract(outDir, component);

    /// <summary>The deterministic checks of the package, cross-referenced with the manifests where they exist.</summary>
    public ExtractionAnswer Verify(string outDir)
    {
        var resolvedOut = ExtractionManifests.ResolvePath(outDir);
        var analysis = ReadAnalysis(resolvedOut);
        var status = manifests.Read<DsStatusManifest>(ExtractionManifests.StatusPath(resolvedOut));

        var report = verifier.Verify(resolvedOut, analysis, status);

        var answer = new JsonObject
        {
            ["outDir"] = resolvedOut,
            ["passed"] = report.HasPassed,
            ["checks"] = Serialize(report.Checks),
            ["figmaRoundTrip"] = new JsonObject
            {
                ["ready"] = report.IsFigmaRoundTripReady,
                ["how"] = ToJsonArray(report.FigmaRoundTripHow),
            },
        };

        return ExtractionAnswer.Ok(answer);
    }

    /// <summary>The porting progress, after recording one component's status when given.</summary>
    public ExtractionAnswer Status(string outDir, string? component, string? status, string? notes, string? storyFile) =>
        progress.Update(outDir, component, status, notes, storyFile);

    private void WriteTokens(string resolvedOut, IReadOnlyList<string> formats, ExtractTokensResult extraction, DsTokensReport report)
    {
        var written = tokenFiles.Write(extraction.Document, resolvedOut, TokenFileWriter.WithCanonicalFirst(formats));

        report.FilesWritten.AddRange(written.FilesWritten);
        report.Warnings.AddRange(written.Warnings);

        manifests.Write(Path.Combine(ExtractionManifests.Directory(resolvedOut), ExtractionManifests.TokensReportFile), report);

        var modes = TokenFileWriter.ModeNames(extraction.Document);

        if (modes.Count > 1)
        {
            manifests.Write(Path.Combine(ExtractionManifests.Directory(resolvedOut), ExtractionManifests.TokenModesFile), modes);
        }
    }

    private DsAnalysis? ReadAnalysis(string resolvedOut) =>
        manifests.Read<DsAnalysis>(ExtractionManifests.AnalysisPath(resolvedOut));

    private static ExtractionAnswer NoAnalysis(string resolvedOut) =>
        ExtractionAnswer.Fail(
            $"There is no analysis manifest: {ExtractionManifests.AnalysisPath(resolvedOut)}.",
            "Run figma_ds_analyze with the same outDir first.");

    /// <summary>Default output directory — <c>design-system</c> inside the first target.</summary>
    private static string ResolveOutDir(string? outDir, IReadOnlyList<string> roots) =>
        string.IsNullOrWhiteSpace(outDir)
            ? Path.Combine(roots[0], "design-system")
            : ExtractionManifests.ResolvePath(outDir);

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
        var formatter = TokenFormatters.Find("dtcg")!;
        var file = formatter.Write(document, new FormatOptions()).Files.FirstOrDefault();

        return file is null ? null : JsonNode.Parse(file.Content);
    }
}
