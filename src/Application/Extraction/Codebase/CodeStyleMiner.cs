using System.Globalization;
using System.Text.RegularExpressions;
using Snail.MCP.Figma.Application.Tokens;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>A variable mined from code.</summary>
public sealed class MinedVariable
{
    /// <summary>Name without the leading <c>--</c> or <c>$</c>.</summary>
    public required string Name { get; init; }

    public required string Value { get; init; }

    /// <summary>Theme mode of the declaration: <c>light</c> for the base, otherwise the mode named by the selector.</summary>
    public string Mode { get; init; } = "light";

    /// <summary><c>path:line</c> form.</summary>
    public required string Source { get; init; }

    public string? Origin { get; init; }

    /// <summary>Mined from actual usage rather than declared explicitly.</summary>
    public bool IsInferred { get; init; }

    public int? Frequency { get; init; }
}

/// <summary>Typography rule mined from styles.</summary>
public sealed record MinedTypographyRule(string Selector, Dictionary<string, string> Properties, string Source);

/// <summary>Raw value and how often it occurs.</summary>
public sealed class RawFrequencyEntry
{
    public required string Value { get; init; }

    public required string Type { get; init; }

    public int Frequency { get; set; }

    public List<string> Sources { get; init; } = [];
}

/// <summary>Mines de-facto styling decisions from sources without executing any project file.</summary>
public static partial class CodeStyleMiner
{
    private const int MaxSourcesPerToken = 6;

    [GeneratedRegex(@"^#([0-9a-fA-F]{3,8})$")]
    private static partial Regex HexColorPattern();

    [GeneratedRegex(@"^rgba?\(\s*(\d+(?:\.\d+)?)[,\s]+(\d+(?:\.\d+)?)[,\s]+(\d+(?:\.\d+)?)(?:[,\s/]+(\d*(?:\.\d+)?%?))?\s*\)$")]
    private static partial Regex RgbColorPattern();

    [GeneratedRegex(@"^hsla?\(\s*(-?\d+(?:\.\d+)?)(?:deg)?[,\s]+(\d+(?:\.\d+)?)%[,\s]+(\d+(?:\.\d+)?)%(?:[,\s/]+[\d.]+%?)?\s*\)$")]
    private static partial Regex HslColorPattern();

    /// <summary>shadcn convention: <c>--primary: 222.2 47.4% 11.2%;</c> — bare HSL components.</summary>
    [GeneratedRegex(@"^(-?\d+(?:\.\d+)?)(?:deg)?[,\s]+(\d+(?:\.\d+)?)%[,\s]+(\d+(?:\.\d+)?)%$")]
    private static partial Regex HslTriplePattern();

    [GeneratedRegex(@"^(oklch|oklab|lab|lch|color)\(")]
    private static partial Regex ModernColorFunctionPattern();

    [GeneratedRegex(@"^-?\d*\.?\d+(px|rem|em|%|vh|vw|pt|ch)$")]
    private static partial Regex DimensionPattern();

    [GeneratedRegex(@"^-?\d*\.?\d+(ms|s)$")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"^-?\d*\.?\d+$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"(font-family|font-sans|font-serif|font-mono|typeface)", RegexOptions.IgnoreCase)]
    private static partial Regex FontFamilyNamePattern();

    [GeneratedRegex(@"(^|-)font(-|$)", RegexOptions.IgnoreCase)]
    private static partial Regex FontPrefixedNamePattern();

    [GeneratedRegex(@",|\bsans\b|\bserif\b|\bmono(space)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex FontStackValuePattern();

    [GeneratedRegex(@"^\d+(px)?\s+\d+(px)?")]
    private static partial Regex ShadowValuePattern();

    [GeneratedRegex(@"shadow", RegexOptions.IgnoreCase)]
    private static partial Regex ShadowNamePattern();

    [GeneratedRegex(
        @"(color|colour|bg|background|foreground|border|ring|fill|stroke|accent|primary|secondary|muted|destructive|success|warning|danger|info|brand)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ColorishNamePattern();

    [GeneratedRegex(@"\[data-(?:theme|mode|color-scheme)=[""']?([\w-]+)[""']?\]")]
    private static partial Regex ThemeAttributePattern();

    [GeneratedRegex(@"prefers-color-scheme:\s*(\w+)")]
    private static partial Regex PrefersColorSchemePattern();

    [GeneratedRegex(@"\.theme-([\w-]+)")]
    private static partial Regex ThemeClassPattern();

    [GeneratedRegex(@"\.([\w-]+?)(?:-mode)?\s*(?:[,{\[:\s]|$)")]
    private static partial Regex ModeClassPattern();

    [GeneratedRegex(
        @"(:root[^{]*|@theme[^{]*|html[^{]*|\.(?:dark|light)(?:-mode)?[^{]*|\.theme-[\w-]+[^{]*|\[data-(?:theme|mode|color-scheme)=[^\]]+\][^{]*|@media[^{]*prefers-color-scheme[^{]*)\{")]
    private static partial Regex ThemeBlockPattern();

    [GeneratedRegex(@"--([\w-]+)\s*:\s*([^;]+);")]
    private static partial Regex CustomPropertyPattern();

    [GeneratedRegex(@"^\$([\w-]+)\s*:\s*([^;]+?)\s*(?:!default)?\s*;", RegexOptions.Multiline)]
    private static partial Regex ScssVariablePattern();

    [GeneratedRegex(@"\btheme\s*:")]
    private static partial Regex TailwindThemeKeyPattern();

    [GeneratedRegex(@"['""]?([\w-]+)['""]?\s*:\s*['""](#[0-9a-fA-F]{3,8}|rgba?\([^)]*\)|hsla?\([^)]*\)|-?\d*\.?\d+(?:px|rem|em|%)?)['""]")]
    private static partial Regex TailwindLiteralPattern();

    [GeneratedRegex(@"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlockPattern();

    [GeneratedRegex(
        @"(^|[\s,.:>~+(\[])h[1-6]\b|head(ing|line)|display|title|lead|eyebrow|caption|prose|subtitle|overline|body-|text-|blockquote",
        RegexOptions.IgnoreCase)]
    private static partial Regex TypographySelectorPattern();

    [GeneratedRegex(@"([^{}@]+)\{([^{}]*)\}")]
    private static partial Regex FlatCssRulePattern();

    [GeneratedRegex(@"([a-z-]+)\s*:\s*([^;]+);")]
    private static partial Regex CssDeclarationPattern();

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|rgba?\([^)]{1,50}\)|hsla?\([^)]{1,50}\)")]
    private static partial Regex AnyColorLiteralPattern();

    [GeneratedRegex(
        @"(margin|padding|gap|top|right|bottom|left|width|height|border-radius|font-size|line-height|letter-spacing)[^:;{}]*:\s*([^;{}]+);")]
    private static partial Regex DimensionDeclarationPattern();

    [GeneratedRegex(@"-?\d*\.?\d+(?:px|rem|em)\b")]
    private static partial Regex DimensionValuePattern();

    [GeneratedRegex(@"class(?:Name)?\s*=\s*(?:""([^""]*)""|'([^']*)'|\{`([^`]*)`\})")]
    private static partial Regex ClassAttributePattern();

    [GeneratedRegex(@"^(?:[\w-]+:)+")]
    private static partial Regex UtilityVariantPrefixPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    [GeneratedRegex(@"^(?:bg|text|border|ring|fill|stroke|from|via|to|divide|outline|accent|caret|decoration)-([a-z]+)-(\d{2,3})$")]
    private static partial Regex ColorUtilityPattern();

    [GeneratedRegex(@"^(?:bg|text|border|ring|fill|stroke)-(white|black)$")]
    private static partial Regex NamedColorUtilityPattern();

    [GeneratedRegex(@"^(?:p|px|py|pt|pr|pb|pl|m|mx|my|mt|mr|mb|ml|gap|gap-x|gap-y|space-x|space-y|size)-(\d+(?:\.\d+)?)$")]
    private static partial Regex SpacingUtilityPattern();

    [GeneratedRegex(@"^rounded-(xs|sm|md|lg|xl|2xl|3xl|4xl|full)$")]
    private static partial Regex RadiusUtilityPattern();

    [GeneratedRegex(@"^text-(xs|sm|base|lg|xl|\dxl)$")]
    private static partial Regex TextSizeUtilityPattern();

    [GeneratedRegex(@"^font-(thin|extralight|light|normal|medium|semibold|bold|extrabold|black)$")]
    private static partial Regex FontWeightUtilityPattern();

    [GeneratedRegex(@"^shadow-(2xs|xs|sm|md|lg|xl|2xl)$")]
    private static partial Regex ShadowUtilityPattern();

    /// <summary>CSS properties related to typography.</summary>
    private static readonly HashSet<string> TypographyProperties = new(StringComparer.Ordinal)
    {
        "font-size", "font-family", "font-weight", "line-height", "letter-spacing",
        "text-transform", "font-style", "text-decoration",
    };

    public static string HslToHex(double h, double s, double l)
    {
        var saturation = s / 100.0;
        var lightness = l / 100.0;
        var c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var hp = ((h % 360) + 360) % 360 / 60.0;
        var x = c * (1 - Math.Abs((hp % 2) - 1));

        var (r, g, b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        var m = lightness - (c / 2);

        return $"#{ToHex2((r + m) * 255)}{ToHex2((g + m) * 255)}{ToHex2((b + m) * 255)}";
    }

    private static string ToHex2(double value) =>
        ((int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255)).ToString("x2", CultureInfo.InvariantCulture);

    private static double ParseNumber(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Normalizes a color literal to lowercase hex; <c>null</c> when it is not a color. Modern functions pass through untouched.</summary>
    public static string? NormalizeColor(string raw)
    {
        var value = raw.Trim();

        if (HexColorPattern().Match(value) is { Success: true } hex)
        {
            var digits = hex.Groups[1].Value.ToLowerInvariant();

            if (digits.Length is 3 or 4)
            {
                digits = string.Concat(digits.Select(c => $"{c}{c}"));
            }

            return $"#{digits}";
        }

        if (RgbColorPattern().Match(value) is { Success: true } rgb)
        {
            var basis = $"#{ToHex2(ParseNumber(rgb.Groups[1].Value))}{ToHex2(ParseNumber(rgb.Groups[2].Value))}{ToHex2(ParseNumber(rgb.Groups[3].Value))}";
            var alphaText = rgb.Groups[4].Value;

            if (alphaText.Length > 0 && alphaText != "1")
            {
                var alpha = alphaText.EndsWith('%')
                    ? ParseNumber(alphaText[..^1]) / 100.0
                    : ParseNumber(alphaText);

                if (alpha < 1)
                {
                    return $"{basis}{ToHex2(alpha * 255)}";
                }
            }

            return basis;
        }

        if (HslColorPattern().Match(value) is { Success: true } hsl)
        {
            return HslToHex(ParseNumber(hsl.Groups[1].Value), ParseNumber(hsl.Groups[2].Value), ParseNumber(hsl.Groups[3].Value));
        }

        if (HslTriplePattern().Match(value) is { Success: true } triple)
        {
            return HslToHex(ParseNumber(triple.Groups[1].Value), ParseNumber(triple.Groups[2].Value), ParseNumber(triple.Groups[3].Value));
        }

        return ModernColorFunctionPattern().IsMatch(value) ? value : null;
    }

    public static bool IsShadcnHslTriple(string value) => HslTriplePattern().IsMatch(value.Trim());

    /// <summary>Token type and literal from name and value; string is the default type.</summary>
    public static (string Type, string Literal) InferTokenType(string name, string value)
    {
        var v = value.Trim();

        if (NormalizeColor(v) is { } color)
        {
            return (TokenTypes.Color, color);
        }

        if (DimensionPattern().IsMatch(v))
        {
            return (TokenTypes.Dimension, v);
        }

        if (DurationPattern().IsMatch(v))
        {
            return (TokenTypes.Duration, v);
        }

        if (NumberPattern().IsMatch(v))
        {
            return (TokenTypes.Number, v);
        }

        if (FontFamilyNamePattern().IsMatch(name)
            || (FontPrefixedNamePattern().IsMatch(name) && FontStackValuePattern().IsMatch(v)))
        {
            return (TokenTypes.FontFamily, v);
        }

        if (ShadowValuePattern().IsMatch(v) && ShadowNamePattern().IsMatch(name))
        {
            return (TokenTypes.Shadow, v);
        }

        return (TokenTypes.String, v);
    }

    public static bool HasColorishName(string name) => ColorishNamePattern().IsMatch(name);

    /// <summary>Theme-block selector into a mode name, covering <c>:root</c>, <c>[data-theme]</c>, <c>.dark</c>, <c>.theme-X</c> and media queries.</summary>
    public static string ClassifyThemeHeader(string header)
    {
        if (ThemeAttributePattern().Match(header) is { Success: true } attribute)
        {
            return attribute.Groups[1].Value.ToLowerInvariant();
        }

        if (PrefersColorSchemePattern().Match(header) is { Success: true } media)
        {
            return media.Groups[1].Value.ToLowerInvariant();
        }

        if (ThemeClassPattern().Match(header) is { Success: true } themeClass)
        {
            return themeClass.Groups[1].Value.ToLowerInvariant();
        }

        if (ModeClassPattern().Match(header) is { Success: true } modeClass
            && modeClass.Groups[1].Value is "dark" or "light")
        {
            return modeClass.Groups[1].Value;
        }

        return "light";
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        var limit = Math.Min(index, text.Length);

        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>Custom properties from CSS and SCSS; light and dark blocks become multi-mode tokens.</summary>
    public static List<MinedVariable> MineCssCustomProperties(string text, string relativePath)
    {
        var result = new List<MinedVariable>();

        var consumed = new List<(int Start, int End)>();

        foreach (Match match in ThemeBlockPattern().Matches(text))
        {
            if (consumed.Any(range => match.Index >= range.Start && match.Index < range.End))
            {
                continue;
            }

            var header = match.Groups[1].Value;
            var mode = ClassifyThemeHeader(header);
            var isTheme = header.StartsWith("@theme", StringComparison.Ordinal);

            var depth = 1;
            var i = match.Index + match.Length;
            var start = i;

            while (i < text.Length && depth > 0)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}') depth--;

                i++;
            }

            consumed.Add((match.Index, i));
            var body = text[start..Math.Max(start, i - 1)];

            foreach (Match declaration in CustomPropertyPattern().Matches(body))
            {
                var value = declaration.Groups[2].Value.Trim();
                result.Add(new MinedVariable
                {
                    Name = declaration.Groups[1].Value,
                    Value = value,
                    Mode = mode,
                    Source = $"{relativePath}:{LineOf(text, start + declaration.Index)}",
                    Origin = isTheme
                        ? "tailwind v4 @theme"
                        : IsShadcnHslTriple(value) ? "shadcn HSL triple" : null,
                });
            }
        }

        return result;
    }

    /// <summary>Top-level SCSS variables (<c>$name: value;</c>); maps and mixins are skipped.</summary>
    public static List<MinedVariable> MineScssVariables(string text, string relativePath)
    {
        var result = new List<MinedVariable>();

        foreach (Match match in ScssVariablePattern().Matches(text))
        {
            result.Add(new MinedVariable
            {
                Name = match.Groups[1].Value,
                Value = match.Groups[2].Value.Trim(),
                Source = $"{relativePath}:{LineOf(text, match.Index)}",
                Origin = "scss variable",
            });
        }

        return result;
    }

    /// <summary>Values from tailwind.config without executing it: the theme object is cut by balanced braces and only literal pairs are mined.</summary>
    public static List<MinedVariable> MineTailwindConfig(string text, string relativePath)
    {
        var themeMatch = TailwindThemeKeyPattern().Match(text);

        if (!themeMatch.Success)
        {
            return [];
        }

        var braceStart = text.IndexOf('{', themeMatch.Index);

        if (braceStart < 0)
        {
            return [];
        }

        var depth = 0;
        var end = -1;

        for (var i = braceStart; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    end = i + 1;
                    break;
                }
            }
        }

        if (end < 0)
        {
            return [];
        }

        var slice = text[braceStart..end];
        var result = new List<MinedVariable>();

        foreach (Match match in TailwindLiteralPattern().Matches(slice))
        {
            result.Add(new MinedVariable
            {
                Name = match.Groups[1].Value,
                Value = match.Groups[2].Value,
                Source = $"{relativePath}:{LineOf(text, braceStart + match.Index)}",
                Origin = "tailwind theme (literal mining)",
            });
        }

        return result;
    }

    /// <summary>Contents of <c>&lt;style&gt;</c> blocks of a .astro / .vue / .svelte single-file component.</summary>
    public static List<string> ExtractStyleBlocks(string text) =>
        [.. StyleBlockPattern().Matches(text).Select(m => m.Groups[1].Value)];

    /// <summary>Typography rules from CSS: typographic selectors with at least one font property.</summary>
    public static List<MinedTypographyRule> MineTypographyStyles(string text, string relativePath)
    {
        var result = new List<MinedTypographyRule>();

        foreach (Match match in FlatCssRulePattern().Matches(text))
        {
            if (result.Count >= 120)
            {
                break;
            }

            var selector = WhitespaceRunPattern().Replace(match.Groups[1].Value.Trim(), " ");

            if (selector.Length > 80 || !TypographySelectorPattern().IsMatch(selector))
            {
                continue;
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match declaration in CssDeclarationPattern().Matches(match.Groups[2].Value))
            {
                if (TypographyProperties.Contains(declaration.Groups[1].Value))
                {
                    properties[declaration.Groups[1].Value] = declaration.Groups[2].Value.Trim();
                }
            }

            if (properties.Count > 0)
            {
                result.Add(new MinedTypographyRule(selector, properties, $"{relativePath}:{LineOf(text, match.Index)}"));
            }
        }

        return result;
    }

    /// <summary>Frequency analysis of raw values; similar colors collapse through <see cref="NormalizeColor"/>.</summary>
    public static void ScanRawValues(
        string text,
        string relativePath,
        bool isStyleFile,
        Dictionary<string, RawFrequencyEntry> accumulator)
    {
        void Bump(string key, string type, string source)
        {
            if (!accumulator.TryGetValue(key, out var entry))
            {
                entry = new RawFrequencyEntry { Value = key, Type = type };
                accumulator[key] = entry;
            }

            entry.Frequency++;

            if (entry.Sources.Count < MaxSourcesPerToken)
            {
                entry.Sources.Add(source);
            }
        }

        foreach (Match match in AnyColorLiteralPattern().Matches(text))
        {
            if (NormalizeColor(match.Value) is { } normalized)
            {
                Bump(normalized, TokenTypes.Color, $"{relativePath}:{LineOf(text, match.Index)}");
            }
        }

        if (!isStyleFile)
        {
            return;
        }

        foreach (Match match in DimensionDeclarationPattern().Matches(text))
        {
            var property = match.Groups[1].Value;
            var source = $"{relativePath}:{LineOf(text, match.Index)}";
            var group = property.Contains("radius", StringComparison.Ordinal) ? "radius"
                : property.Contains("font", StringComparison.Ordinal) ? "font"
                : "space";

            foreach (Match value in DimensionValuePattern().Matches(match.Groups[2].Value))
            {
                Bump($"{group}:{value.Value}", TokenTypes.Dimension, source);
            }
        }
    }

    /// <summary>Parses Tailwind v4 theme.css into a variable-name → value map.</summary>
    public static Dictionary<string, string> ParseTailwindTheme(string css)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in CustomPropertyPattern().Matches(css))
        {
            map[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        return map;
    }

    /// <summary>Counts utility classes in class and className attributes.</summary>
    public static void ScanUtilityClasses(string text, Dictionary<string, int> accumulator)
    {
        foreach (Match match in ClassAttributePattern().Matches(text))
        {
            var raw = match.Groups[1].Success ? match.Groups[1].Value
                : match.Groups[2].Success ? match.Groups[2].Value
                : match.Groups[3].Value;

            foreach (var token in raw.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var cls = UtilityVariantPrefixPattern().Replace(token, string.Empty);

                if (cls.Length > 0)
                {
                    accumulator[cls] = accumulator.GetValueOrDefault(cls) + 1;
                }
            }
        }
    }

    /// <summary>One counted utility class into a mined variable, valued from the app's own Tailwind theme; <c>null</c> for non-styling utilities.</summary>
    public static MinedVariable? UtilityToMinedVariable(string cls, int count, Dictionary<string, string> theme)
    {
        MinedVariable Make(string name, string value) => new()
        {
            Name = name,
            Value = value,
            Source = "tailwind utility usage",
            Origin = $"tailwind utility '{cls}' x{count} (de facto usage)",
            IsInferred = true,
            Frequency = count,
        };

        var color = ColorUtilityPattern().Match(cls);

        if (color.Success)
        {
            var key = $"color-{color.Groups[1].Value}-{color.Groups[2].Value}";

            return theme.TryGetValue(key, out var value) ? Make(key, value) : null;
        }

        var namedColor = NamedColorUtilityPattern().Match(cls);

        if (namedColor.Success)
        {
            var name = namedColor.Groups[1].Value;

            return Make($"color-{name}", name == "white" ? "#ffffff" : "#000000");
        }

        var spacing = SpacingUtilityPattern().Match(cls);

        if (spacing.Success)
        {
            var n = ParseNumber(spacing.Groups[1].Value);

            return n is > 0 and <= 96
                ? Make($"spacing-{spacing.Groups[1].Value}", $"{(n * 0.25).ToString(CultureInfo.InvariantCulture)}rem")
                : null;
        }

        var radius = RadiusUtilityPattern().Match(cls);

        if (radius.Success)
        {
            var key = radius.Groups[1].Value;
            var value = theme.GetValueOrDefault($"radius-{key}") ?? (key == "full" ? "9999px" : null);

            return value is null ? null : Make($"radius-{key}", value);
        }

        var textSize = TextSizeUtilityPattern().Match(cls);

        if (textSize.Success)
        {
            var key = textSize.Groups[1].Value;

            return theme.TryGetValue($"text-{key}", out var value) ? Make($"font-size-{key}", value) : null;
        }

        var weight = FontWeightUtilityPattern().Match(cls);

        if (weight.Success)
        {
            var key = weight.Groups[1].Value;

            return theme.TryGetValue($"font-weight-{key}", out var value) ? Make($"font-weight-{key}", value) : null;
        }

        var shadow = ShadowUtilityPattern().Match(cls);

        if (shadow.Success)
        {
            var key = shadow.Groups[1].Value;

            return theme.TryGetValue($"shadow-{key}", out var value) ? Make($"shadow-{key}", value) : null;
        }

        return null;
    }
}
