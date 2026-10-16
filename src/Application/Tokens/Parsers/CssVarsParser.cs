using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Tokens.Parsers;

/// <summary>CSS custom properties; selector blocks become modes (<c>:root</c>, <c>.dark</c>, <c>[data-theme="x"]</c>).</summary>
public sealed partial class CssVarsParser : ITokenParser
{
    [GeneratedRegex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Singleline)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"--(?<name>[A-Za-z0-9_-]+)\s*:\s*(?<value>[^;]+);")]
    private static partial Regex DeclarationPattern();

    [GeneratedRegex(@"^var\(\s*--(?<name>[A-Za-z0-9_-]+)\s*\)$")]
    private static partial Regex VarReferencePattern();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentPattern();

    [GeneratedRegex(@"data-theme\s*=\s*[""']?(?<name>[^""'\]]+)")]
    private static partial Regex DataThemePattern();

    /// <summary>Configuration name of this format, <c>css-vars</c>.</summary>
    public string Format => "css-vars";

    /// <summary>Reads CSS custom properties into the context set, one mode per selector block.</summary>
    public ParseResult Parse(string content, ParseContext context)
    {
        var warnings = new List<string>();
        var set = context.Document.GetOrAddSet(context.SetName);

        var stripped = BlockCommentPattern().Replace(content, string.Empty);

        var byPath = new Dictionary<string, Token>(StringComparer.Ordinal);

        foreach (var token in set.Tokens)
        {
            byPath[token.SlashPath] = token;
        }

        var matched = false;

        foreach (Match block in BlockPattern().Matches(stripped))
        {
            matched = true;
            var mode = ModeFor(block.Groups["selector"].Value.Trim(), context.DefaultMode);

            foreach (Match declaration in DeclarationPattern().Matches(block.Groups["body"].Value))
            {
                AddToken(set, byPath, mode, declaration.Groups["name"].Value, declaration.Groups["value"].Value.Trim());
            }
        }

        if (!matched)
        {
            foreach (Match declaration in DeclarationPattern().Matches(stripped))
            {
                AddToken(set, byPath, context.DefaultMode, declaration.Groups["name"].Value,
                    declaration.Groups["value"].Value.Trim());
            }
        }

        if (set.Tokens.Count == 0)
        {
            warnings.Add("The file declares no --variable at all.");
        }

        if (set.Modes.Count == 0)
        {
            set.Modes.Add(context.DefaultMode);
        }

        return new ParseResult(context.Document, warnings);
    }

    /// <summary>Selector → mode name, the inverse of what the formatter does.</summary>
    internal static string ModeFor(string selector, string defaultMode)
    {
        if (selector.Contains(":root", StringComparison.Ordinal))
        {
            return defaultMode;
        }

        if (selector.Contains(".dark", StringComparison.OrdinalIgnoreCase))
        {
            return "Dark";
        }

        var themeMatch = DataThemePattern().Match(selector);

        return themeMatch.Success ? themeMatch.Groups["name"].Value : defaultMode;
    }

    private static void AddToken(
        TokenSet set,
        Dictionary<string, Token> byPath,
        string mode,
        string name,
        string rawValue)
    {
        var path = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var key = string.Join('/', path);

        if (!byPath.TryGetValue(key, out var token))
        {
            token = new Token { Path = path, Type = InferType(rawValue) };
            byPath[key] = token;
            set.Tokens.Add(token);
        }

        var varMatch = VarReferencePattern().Match(rawValue);
        token.Values[mode] = varMatch.Success
            ? TokenValue.FromReference(varMatch.Groups["name"].Value.Replace('-', '.'))
            : TokenValue.FromLiteral(rawValue);

        if (!set.Modes.Contains(mode, StringComparer.Ordinal))
        {
            set.Modes.Add(mode);
        }
    }

    private static string InferType(string value)
    {
        if (value.StartsWith('#') || value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("hsl", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("oklch", StringComparison.OrdinalIgnoreCase))
        {
            return TokenTypes.Color;
        }

        if (value.Contains("px", StringComparison.Ordinal) || value.Contains("rem", StringComparison.Ordinal))
        {
            return TokenTypes.Dimension;
        }

        return double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _)
            ? TokenTypes.Number
            : TokenTypes.String;
    }
}
