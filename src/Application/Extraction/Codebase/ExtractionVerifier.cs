using System.Text.RegularExpressions;
using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Parsers;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>One verification check.</summary>
public sealed class VerifyCheck
{
    /// <summary>What the check verified, phrased as the claim it tests.</summary>
    public required string Name { get; init; }

    /// <summary><c>pass</c>, <c>fail</c>, <c>warn</c> or <c>skip</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The offending files, variables or values behind the outcome, capped at twenty entries.</summary>
    public List<string> Details { get; init; } = [];
}

/// <summary>The outcome of verifying an extracted design system and the way to mirror it into Figma.</summary>
public sealed class VerifyReport
{
    /// <summary>Whether no check failed; warnings and skips do not count against it.</summary>
    public bool HasPassed { get; set; }

    /// <summary>Every check that ran, in the order it ran.</summary>
    public List<VerifyCheck> Checks { get; init; } = [];

    /// <summary>Whether every check passed and a token file exists, so the system can be mirrored into Figma.</summary>
    public bool IsFigmaRoundTripReady { get; set; }

    /// <summary>The steps that move the tokens and components into Figma, should design lead.</summary>
    public List<string> FigmaRoundTripHow { get; init; } = [];
}

/// <summary>Deterministic checks of an extracted design system, each encoding a failure class seen on real runs.</summary>
public sealed partial class ExtractionVerifier(IWorkspace workspace)
{
    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex CssCommentPattern();

    [GeneratedRegex(@"--[\w-]+:\s*[""'][a-zA-Z-]+\(")]
    private static partial Regex QuotedCssExpressionPattern();

    [GeneratedRegex(@"--([\w-]+)\s*:")]
    private static partial Regex DefinedVariablePattern();

    [GeneratedRegex(@"var\(\s*--([\w-]+)\s*\)")]
    private static partial Regex ConsumedVariablePattern();

    [GeneratedRegex(@"^index\.(js|jsx|ts|tsx)$")]
    private static partial Regex IndexBarrelPattern();

    private const int MaxDetails = 20;

    /// <summary>The checks of the design-system package in <c>outDir</c>, cross-referenced with the analysis and porting status where they exist.</summary>
    public VerifyReport Verify(string outDir, DsAnalysis? analysis, DsStatusManifest? status)
    {
        var report = new VerifyReport();

        void Add(string name, string outcome, IEnumerable<string>? details = null) =>
            report.Checks.Add(new VerifyCheck
            {
                Name = name,
                Status = outcome,
                Details = [.. (details ?? []).Take(MaxDetails)],
            });

        var tokensPath = Path.Combine(outDir, "tokens", "tokens.json");
        var tokensExist = workspace.HasFile(tokensPath);

        if (tokensExist)
        {
            try
            {
                var parsed = new DtcgParser().Parse(
                    workspace.ReadText(tokensPath) ?? string.Empty,
                    new ParseContext("Tokens", new TokenDocument()));
                var document = parsed.Document;

                Add(
                    "tokens.json parses as DTCG (compatible with figma_import_tokens)",
                    "pass",
                    [
                        $"{document.Sets.Count} set(s), {document.TokenCount} tokens, modes: {(document.Sets.FirstOrDefault() is { } first && first.Modes.Count > 0 ? string.Join('/', first.Modes) : "-")}",
                    ]);

                var dangling = DanglingReferences(document);
                Add("every reference resolves (no dangling {refs})", dangling.Count == 0 ? "pass" : "fail", dangling);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
            {
                Add("tokens.json parses as DTCG", "fail", [ex.Message]);
            }
        }
        else
        {
            Add("tokens.json exists", "fail", ["Run figma_ds_extract_tokens."]);
        }

        var cssTargets = new[] { "tokens/tokens.css", "tokens/theme.css", "tokens/_tokens.scss" }
            .Select(relative => Path.Combine(outDir, relative.Replace('/', Path.DirectorySeparatorChar)))
            .Where(workspace.HasFile)
            .ToList();

        var quoted = new List<string>();

        foreach (var file in cssTargets)
        {
            foreach (Match match in QuotedCssExpressionPattern().Matches(workspace.ReadText(file) ?? string.Empty))
            {
                quoted.Add($"{Path.GetFileName(file)}: {match.Value}…");
            }
        }

        Add("the generated token files carry no quoted CSS functional expressions",
            quoted.Count == 0 ? "pass" : "fail",
            quoted);

        var componentsDir = Path.Combine(outDir, "src", "components");
        var cssFiles = new List<string>();
        CollectCss(componentsDir, cssFiles);

        var previewCss = Path.Combine(outDir, ".storybook", "preview.css");

        if (workspace.HasFile(previewCss))
        {
            cssFiles.Add(previewCss);
        }

        var defined = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in cssTargets.Concat(cssFiles))
        {
            foreach (Match match in DefinedVariablePattern().Matches(StripComments(workspace.ReadText(file) ?? string.Empty)))
            {
                defined.Add(match.Groups[1].Value);
            }
        }

        var unresolved = new List<string>();

        foreach (var file in cssFiles)
        {
            foreach (Match match in ConsumedVariablePattern().Matches(StripComments(workspace.ReadText(file) ?? string.Empty)))
            {
                if (!defined.Contains(match.Groups[1].Value))
                {
                    unresolved.Add($"--{match.Groups[1].Value} ({Path.GetRelativePath(outDir, file)})");
                }
            }
        }

        Add("every var() without a fallback resolves in the workshop",
            unresolved.Count == 0 ? "pass" : "fail",
            unresolved.Distinct(StringComparer.Ordinal));

        var structureGaps = new List<string>();
        var componentDirectories = 0;

        if (workspace.HasDirectory(componentsDir))
        {
            foreach (var directory in Directories(componentsDir))
            {
                componentDirectories++;
                var name = Path.GetFileName(directory);
                var files = Files(directory).Select(Path.GetFileName).OfType<string>().ToList();

                if (!files.Any(f => f.Contains(".stories.", StringComparison.Ordinal)))
                {
                    structureGaps.Add($"{name}: no story file");
                }

                if (!files.Any(f => IndexBarrelPattern().IsMatch(f)))
                {
                    structureGaps.Add($"{name}: no index barrel");
                }
            }
        }

        Add($"component structure ({componentDirectories} director(ies): stories + index barrel)",
            componentDirectories == 0 ? "skip" : structureGaps.Count == 0 ? "pass" : "fail",
            structureGaps);

        if (analysis is not null && status is not null)
        {
            var unaccounted = analysis.Components
                .Where(c => !string.Equals(c.Classification, DsClassifications.PureVendor, StringComparison.Ordinal))
                .Where(c => !status.Components.ContainsKey(c.Name))
                .Select(c => c.Name)
                .ToList();

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var entry in status.Components.Values)
            {
                counts[entry.Status] = counts.GetValueOrDefault(entry.Status) + 1;
            }

            Add("every portable component of the inventory has a status",
                unaccounted.Count == 0 ? "pass" : "warn",
                new[] { $"statuses: {string.Join(", ", counts.Select(p => $"{p.Key}={p.Value}"))}" }
                    .Concat(unaccounted.Select(name => $"no status: {name}")));
        }
        else
        {
            Add("porting coverage", "skip", ["the analysis or progress manifest is missing"]);
        }

        report.HasPassed = report.Checks.All(check => check.Status != "fail");

        var pending = status?.Components.Values.Any(e =>
            e.Status is DsPortStatuses.Pending or DsPortStatuses.InProgress) ?? false;

        report.IsFigmaRoundTripReady = report.HasPassed && tokensExist;
        report.FigmaRoundTripHow.AddRange(
        [
            "Ask the team: does code lead (we stop at this Storybook package) or does design lead (we mirror into Figma)?",
            $"Design leads, step 1 — tokens into Figma variables: figma_import_tokens with the contents of '{tokensPath}' "
            + "and dryRun=true for a preview, then dryRun=false. Collections and modes move over as they are.",
            "Design leads, step 2 — components into Figma component sets: figma_create_component_set plus figma_execute, "
            + "binding fills, strokes and radii to the imported variables.",
        ]);

        if (pending)
        {
            report.FigmaRoundTripHow.Add(
                "Some components are still pending or in-progress — finish them or mark them skipped before going into Figma, "
                + "otherwise the canvas will mirror an incomplete set.");
        }

        return report;
    }

    private static string StripComments(string css) => CssCommentPattern().Replace(css, string.Empty);

    private void CollectCss(string directory, List<string> found)
    {
        foreach (var entry in Directories(directory))
        {
            CollectCss(entry, found);
        }

        found.AddRange(Files(directory).Where(file => file.EndsWith(".css", StringComparison.OrdinalIgnoreCase)));
    }

    private IEnumerable<string> Directories(string directory) =>
        workspace.Entries(directory).Where(entry => entry.IsDirectory).Select(entry => entry.Path);

    private IEnumerable<string> Files(string directory) =>
        workspace.Entries(directory).Where(entry => !entry.IsDirectory).Select(entry => entry.Path);

    /// <summary>References without a target: such a document would not survive import into Figma.</summary>
    private static List<string> DanglingReferences(TokenDocument document)
    {
        var resolver = new AliasResolver(document);
        var problems = new List<string>();

        foreach (var token in document.AllTokens)
        {
            foreach (var (mode, value) in token.Values)
            {
                if (!value.IsReference)
                {
                    continue;
                }

                var reference = value.Reference!.Trim('{', '}');

                if (resolver.Resolve(reference) is null)
                {
                    problems.Add($"{token.SlashPath} [{mode}] → {{{reference}}}");
                }
            }
        }

        return problems;
    }
}
