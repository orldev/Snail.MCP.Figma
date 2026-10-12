using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Snail.MCP.Figma.Application.Tokens;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Settings for mining tokens from a codebase.</summary>
public sealed record ExtractTokensOptions
{
    /// <summary>How often a raw value must occur to become a token.</summary>
    public int MinFrequency { get; init; } = 4;

    /// <summary>Cap on inferred tokens per type.</summary>
    public int MaxInferredPerType { get; init; } = 60;

    /// <summary>Application roots; enables mining Tailwind utility classes valued from each root's own theme.css.</summary>
    public IReadOnlyList<string>? TargetRoots { get; init; }
}

/// <summary>What was mined and what remains in doubt.</summary>
public sealed record ExtractTokensResult(
    TokenDocument Document,
    List<string> Warnings,
    List<DsBelowThreshold> BelowThreshold,
    Dictionary<string, int> Counts);

/// <summary>Assembles mined values into the shared token model: declared values always become tokens, inferred ones are promoted by frequency; names stay structural.</summary>
public static partial class CodeTokenExtractor
{
    private const int MaxSourcesPerToken = 6;

    private static readonly HashSet<string> StyleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".scss", ".sass", ".less",
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tsx", ".jsx", ".ts", ".js",
    };

    [GeneratedRegex(@"^var\(\s*--([\w-]+)\s*(?:,[^)]*)?\)$")]
    private static partial Regex VarReferencePattern();

    [GeneratedRegex(@"^hsla?\(\s*var\(\s*--([\w-]+)\s*\)\s*(?:[,/][^)]*)?\)$")]
    private static partial Regex WrappedVarReferencePattern();

    /// <summary>Names that read as a role rather than a scale position.</summary>
    [GeneratedRegex(
        @"^(?:color-|bg-|text-|border-)?(primary|secondary|accent|muted|destructive|success|warning|danger|info|background|foreground|bg|fg|card|popover|border|input|ring|surface|on[A-Z-]|link|brand|chart-\d+|sidebar)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SemanticNamePattern();

    [GeneratedRegex(@"^[a-z]{1,5}-", RegexOptions.IgnoreCase)]
    private static partial Regex BrandPrefixPattern();

    [GeneratedRegex(@"tailwind\.config\.(js|cjs|mjs|ts)$")]
    private static partial Regex TailwindConfigFilePattern();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlockStripPattern();

    [GeneratedRegex(@"^-")]
    private static partial Regex LeadingMinusPattern();

    [GeneratedRegex(@"[^\w.-]")]
    private static partial Regex UnsafeNameCharacterPattern();

    [GeneratedRegex(@"^(-?\d*\.?\d+)(px|rem|em)?$")]
    private static partial Regex LengthValuePattern();

    /// <summary>Mined variable name into a token path, split on dashes and dots.</summary>
    public static string[] NameToPath(string name) =>
        name.Replace('.', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Semantic means a role in the name or any reference value; brand prefixes are stripped first.</summary>
    private static bool IsSemanticToken(string name, TokenValue value)
    {
        if (value.IsReference)
        {
            return true;
        }

        if (SemanticNamePattern().IsMatch(name))
        {
            return true;
        }

        var stripped = BrandPrefixPattern().Replace(name, string.Empty, 1);

        return !string.Equals(stripped, name, StringComparison.Ordinal) && SemanticNamePattern().IsMatch(stripped);
    }

    public static ExtractTokensResult Extract(IReadOnlyList<WalkedFile> files, ExtractTokensOptions? options = null)
    {
        var opts = options ?? new ExtractTokensOptions();
        var warnings = new List<string>();

        var mined = new List<MinedVariable>();
        var rawValues = new Dictionary<string, RawFrequencyEntry>(StringComparer.Ordinal);
        var utilities = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var isStyle = StyleExtensions.Contains(file.Extension);
            var isSingleFileComponent = file.Extension is ".astro" or ".vue" or ".svelte";
            var isCode = CodeExtensions.Contains(file.Extension);

            if (!isStyle && !isCode && !isSingleFileComponent)
            {
                continue;
            }

            var text = CodeWalker.ReadText(file.Path);

            if (text is null)
            {
                continue;
            }

            if (isStyle)
            {
                mined.AddRange(CodeStyleMiner.MineCssCustomProperties(text, file.RelativePath));

                if (file.Extension is ".scss" or ".sass")
                {
                    mined.AddRange(CodeStyleMiner.MineScssVariables(text, file.RelativePath));
                }
            }

            var markup = text;

            if (isSingleFileComponent)
            {
                foreach (var block in CodeStyleMiner.ExtractStyleBlocks(text))
                {
                    mined.AddRange(CodeStyleMiner.MineCssCustomProperties(block, file.RelativePath));
                    CodeStyleMiner.ScanRawValues(block, file.RelativePath, isStyleFile: true, rawValues);
                }

                markup = StyleBlockStripPattern().Replace(text, string.Empty);
            }

            if (TailwindConfigFilePattern().IsMatch(file.RelativePath))
            {
                mined.AddRange(CodeStyleMiner.MineTailwindConfig(text, file.RelativePath));
            }

            CodeStyleMiner.ScanRawValues(markup, file.RelativePath, isStyle, rawValues);

            if (isCode || isSingleFileComponent)
            {
                CodeStyleMiner.ScanUtilityClasses(markup, utilities);
            }
        }

        MineTailwindUtilities(opts, mined, utilities, warnings);

        var byName = new Dictionary<string, List<MinedVariable>>(StringComparer.Ordinal);
        var modeOrder = new List<string> { "light" };

        foreach (var variable in mined)
        {
            if (!byName.TryGetValue(variable.Name, out var variables))
            {
                variables = [];
                byName[variable.Name] = variables;
            }

            variables.Add(variable);

            if (!modeOrder.Contains(variable.Mode, StringComparer.Ordinal))
            {
                modeOrder.Add(variable.Mode);
            }
        }

        var multiMode = modeOrder.Count > 1;

        string ModeLabel(string mode) =>
            multiMode ? char.ToUpperInvariant(mode[0]) + mode[1..] : "Default";

        var modeLabels = multiMode ? modeOrder.Select(ModeLabel).ToList() : ["Default"];

        var primitives = new List<Token>();
        var semantic = new List<Token>();
        var declaredColors = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, variables) in byName)
        {
            var baseVariable = variables.FirstOrDefault(v => v.Mode == "light") ?? variables[0];

            TokenValue MakeValue(MinedVariable variable)
            {
                var trimmed = variable.Value.Trim();

                if (VarReferencePattern().Match(trimmed) is { Success: true } reference
                    && byName.ContainsKey(reference.Groups[1].Value))
                {
                    return TokenValue.FromReference(string.Join('.', NameToPath(reference.Groups[1].Value)));
                }

                if (WrappedVarReferencePattern().Match(trimmed) is { Success: true } wrapped
                    && byName.ContainsKey(wrapped.Groups[1].Value))
                {
                    return TokenValue.FromReference(string.Join('.', NameToPath(wrapped.Groups[1].Value)));
                }

                var (_, literal) = CodeStyleMiner.InferTokenType(variable.Name, variable.Value);

                return TokenValue.FromLiteral(literal);
            }

            var baseValue = MakeValue(baseVariable);

            var typeSource = baseVariable;

            for (var depth = 0; depth < 6; depth++)
            {
                var trimmed = typeSource.Value.Trim();
                var reference = VarReferencePattern().Match(trimmed);

                if (!reference.Success)
                {
                    reference = WrappedVarReferencePattern().Match(trimmed);
                }

                if (!reference.Success || !byName.TryGetValue(reference.Groups[1].Value, out var target))
                {
                    break;
                }

                var targetBase = target.FirstOrDefault(v => v.Mode == "light") ?? target.FirstOrDefault();

                if (targetBase is null)
                {
                    break;
                }

                typeSource = targetBase;
            }

            var (type, _) = CodeStyleMiner.InferTokenType(typeSource.Name, typeSource.Value);

            if (type == TokenTypes.Color && baseValue.Literal is JsonValue colorLiteral
                && colorLiteral.TryGetValue<string>(out var colorText))
            {
                declaredColors.Add(colorText);
            }

            var provenance = new DsTokenProvenance
            {
                Confidence = baseVariable.IsInferred ? "inferred" : "declared",
                Sources = [.. variables.Take(MaxSourcesPerToken).Select(v => v.Source)],
                Frequency = baseVariable.Frequency,
                Note = baseVariable.Origin,
            };

            var values = new Dictionary<string, TokenValue>(StringComparer.Ordinal);

            foreach (var mode in multiMode ? modeOrder : ["light"])
            {
                var declared = variables.FirstOrDefault(v => string.Equals(v.Mode, mode, StringComparison.Ordinal));
                values[ModeLabel(mode)] = declared is null ? baseValue : MakeValue(declared);
            }

            var path = NameToPath(name);

            if (path.Length == 0)
            {
                continue;
            }

            var token = new Token
            {
                Path = path,
                Type = type,
                Values = values,
                ForeignExtensions = ProvenanceExtensions(provenance),
            };

            (IsSemanticToken(name, baseValue) ? semantic : primitives).Add(token);
        }

        var belowThreshold = new List<DsBelowThreshold>();
        var inferredByType = new Dictionary<string, List<RawFrequencyEntry>>(StringComparer.Ordinal);

        foreach (var entry in rawValues.Values)
        {
            if (entry.Type == TokenTypes.Color && declaredColors.Contains(entry.Value))
            {
                continue;
            }

            if (entry.Frequency < opts.MinFrequency)
            {
                if (entry.Frequency > 1)
                {
                    belowThreshold.Add(new DsBelowThreshold
                    {
                        Value = entry.Value,
                        Type = entry.Type,
                        Frequency = entry.Frequency,
                    });
                }

                continue;
            }

            if (!inferredByType.TryGetValue(entry.Type, out var entries))
            {
                entries = [];
                inferredByType[entry.Type] = entries;
            }

            entries.Add(entry);
        }

        belowThreshold.Sort((a, b) => b.Frequency.CompareTo(a.Frequency));

        var seenPaths = new HashSet<string>(
            primitives.Concat(semantic).Select(t => t.SlashPath),
            StringComparer.Ordinal);

        foreach (var (type, entries) in inferredByType)
        {
            entries.Sort((a, b) => b.Frequency.CompareTo(a.Frequency));
            var kept = entries.Take(opts.MaxInferredPerType).ToList();

            if (entries.Count > kept.Count)
            {
                warnings.Add(
                    $"{entries.Count - kept.Count} inferred values of type {type} did not fit the per-type cap and stayed in belowThreshold.");
                belowThreshold.AddRange(entries.Skip(opts.MaxInferredPerType).Select(e => new DsBelowThreshold
                {
                    Value = e.Value,
                    Type = e.Type,
                    Frequency = e.Frequency,
                }));
            }

            var promoted = 0;

            foreach (var entry in kept)
            {
                string[] path;
                string literal;

                if (type == TokenTypes.Color)
                {
                    path = ["color", "extracted", entry.Value.StartsWith('#') ? $"hex-{entry.Value[1..]}" : entry.Value];
                    literal = entry.Value;
                }
                else
                {
                    var parts = entry.Value.Split(':', 2);
                    var group = parts[0];
                    literal = parts.Length > 1 ? parts[1] : entry.Value;

                    var sanitized = UnsafeNameCharacterPattern().Replace(
                        LeadingMinusPattern().Replace(literal, "neg-"),
                        string.Empty);

                    path =
                    [
                        group switch { "space" => "spacing", "font" => "fontSize", _ => group },
                        sanitized,
                    ];
                }

                var slashPath = string.Join('/', path);

                if (!seenPaths.Add(slashPath))
                {
                    warnings.Add($"Inferred {type} value '{entry.Value}' skipped — the path {slashPath} is already taken.");
                    continue;
                }

                promoted++;
                primitives.Add(new Token
                {
                    Path = path,
                    Type = type,
                    Values = modeLabels.ToDictionary(
                        label => label,
                        _ => TokenValue.FromLiteral(literal),
                        StringComparer.Ordinal),
                    ForeignExtensions = ProvenanceExtensions(new DsTokenProvenance
                    {
                        Confidence = "inferred",
                        Frequency = entry.Frequency,
                        Sources = entry.Sources,
                        Note = "promoted by frequency — rename it meaningfully before release",
                    }),
                });
            }

            if (promoted > 0)
            {
                warnings.Add(
                    $"Promoted {promoted} inferred values of type {type} (frequency >= {opts.MinFrequency}). Review the names before release.");
            }
        }

        var document = new TokenDocument();

        if (primitives.Count > 0)
        {
            document.Sets.Add(new TokenSet
            {
                Name = "Primitives",
                Description = "Structural tokens from the codebase: declared variables plus raw values promoted by frequency.",
                Modes = [.. modeLabels],
                Tokens = primitives,
            });
        }

        if (semantic.Count > 0)
        {
            document.Sets.Add(new TokenSet
            {
                Name = "Semantic",
                Description = "Role-named tokens from the codebase: primary, background, border and the rest.",
                Modes = [.. modeLabels],
                Tokens = semantic,
            });
        }

        if (document.Sets.Count == 0)
        {
            warnings.Add(
                "No tokens found. The app may be styled entirely inline — try lowering minFrequency or check the include/exclude filters.");
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var token in document.AllTokens)
        {
            counts[token.Type] = counts.GetValueOrDefault(token.Type) + 1;
        }

        return new ExtractTokensResult(
            document,
            warnings,
            [.. belowThreshold.Take(100)],
            counts);
    }

    /// <summary>Frequency mining of Tailwind utility classes from the app's own theme.</summary>
    private static void MineTailwindUtilities(
        ExtractTokensOptions options,
        List<MinedVariable> mined,
        Dictionary<string, int> utilities,
        List<string> warnings)
    {
        if (options.TargetRoots is not { Count: > 0 } || utilities.Count == 0)
        {
            return;
        }

        Dictionary<string, string>? theme = null;

        foreach (var root in options.TargetRoots)
        {
            var themeText = CodeWalker.ReadText(Path.Combine(root, "node_modules", "tailwindcss", "theme.css"));

            if (themeText is not null)
            {
                theme = CodeStyleMiner.ParseTailwindTheme(themeText);
                break;
            }
        }

        if (theme is null)
        {
            return;
        }

        var promoted = 0;

        foreach (var (cls, count) in utilities)
        {
            if (count < options.MinFrequency)
            {
                continue;
            }

            var entry = CodeStyleMiner.UtilityToMinedVariable(cls, count, theme);

            if (entry is not null && !mined.Any(v => string.Equals(v.Name, entry.Name, StringComparison.Ordinal)))
            {
                mined.Add(entry);
                promoted++;
            }
        }

        if (promoted > 0)
        {
            warnings.Add(
                $"Promoted {promoted} values of Tailwind utility classes (frequency >= {options.MinFrequency}) " +
                "from the app's own theme.css — this is the de facto palette and scale, review the names before release.");
        }
    }

    private static JsonObject ProvenanceExtensions(DsTokenProvenance provenance) =>
        new()
        {
            [DsTokenProvenance.ExtensionKey] =
                JsonSerializer.SerializeToNode(provenance, DsJson.Options),
        };

    /// <summary>Reads provenance back from token extensions; <c>null</c> when absent.</summary>
    public static DsTokenProvenance? ReadProvenance(Token token)
    {
        if (token.ForeignExtensions?[DsTokenProvenance.ExtensionKey] is not { } node)
        {
            return null;
        }

        try
        {
            return node.Deserialize<DsTokenProvenance>(DsJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Approximate pixels from "12px" / "0.5rem" / "16" — for proportional showcase bars.</summary>
    public static double? ApproximatePixels(string value)
    {
        var match = LengthValuePattern().Match(value.Trim());

        if (!match.Success)
        {
            return null;
        }

        var number = double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);

        return match.Groups[2].Value is "rem" or "em" ? number * 16 : number;
    }
}
