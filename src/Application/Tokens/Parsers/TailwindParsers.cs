using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Tokens.Parsers;

/// <summary>Tailwind v4: the theme lives in a <c>@theme</c> CSS block, parsed as CSS variables inside it.</summary>
public sealed partial class TailwindV4Parser : ITokenParser
{
    [GeneratedRegex(@"@theme[^{]*\{(?<body>.*?)\n\}", RegexOptions.Singleline)]
    private static partial Regex ThemeBlockPattern();

    public string Format => "tailwind-v4";

    public ParseResult Parse(string content, ParseContext context)
    {
        var match = ThemeBlockPattern().Match(content);

        var body = match.Success ? $":root {{{match.Groups["body"].Value}}}" : content;
        var result = new CssVarsParser().Parse(body, context);

        var warnings = result.Warnings.ToList();

        if (!match.Success)
        {
            warnings.Add("No @theme block found — the file was parsed as plain CSS variables.");
        }

        return new ParseResult(result.Document, warnings);
    }
}

/// <summary>Tailwind v3: the theme lives in <c>tailwind.config.js</c> under <c>theme.extend</c>; the object literal is read as JSON without executing JS.</summary>
public sealed partial class TailwindV3Parser : ITokenParser
{
    [GeneratedRegex(@"(extend\s*:\s*)?(?<key>colors|spacing|fontSize|borderRadius|boxShadow|fontFamily)\s*:\s*\{")]
    private static partial Regex SectionPattern();

    /// <summary>Tailwind config sections → path prefix and token type.</summary>
    private static readonly Dictionary<string, (string Prefix, string Type)> Sections = new(StringComparer.Ordinal)
    {
        ["colors"] = ("color", TokenTypes.Color),
        ["spacing"] = ("spacing", TokenTypes.Dimension),
        ["fontSize"] = ("font-size", TokenTypes.Dimension),
        ["borderRadius"] = ("radius", TokenTypes.Dimension),
        ["boxShadow"] = ("shadow", TokenTypes.Shadow),
        ["fontFamily"] = ("font-family", TokenTypes.FontFamily),
    };

    public string Format => "tailwind-v3";

    public ParseResult Parse(string content, ParseContext context)
    {
        var warnings = new List<string>();
        var set = context.Document.GetOrAddSet(context.SetName);

        foreach (Match match in SectionPattern().Matches(content))
        {
            var key = match.Groups["key"].Value;
            var braceStart = content.IndexOf('{', match.Index + match.Length - 1);
            var body = ExtractBalanced(content, braceStart);

            if (body is null)
            {
                warnings.Add($"Section {key}: could not find the closing brace.");
                continue;
            }

            var json = ToJson(body);

            if (JsonNode.Parse(json) is not JsonObject parsed)
            {
                warnings.Add($"Section {key}: the contents did not reduce to JSON, skipping.");
                continue;
            }

            var (prefix, type) = Sections[key];
            Walk(parsed, [prefix], type, set, context);
        }

        if (set.Modes.Count == 0)
        {
            set.Modes.Add(context.DefaultMode);
        }

        if (set.Tokens.Count == 0)
        {
            warnings.Add("The config declares no theme sections (colors, spacing, fontSize and the like).");
        }

        return new ParseResult(context.Document, warnings);
    }

    private static void Walk(JsonObject node, List<string> path, string type, TokenSet set, ParseContext context)
    {
        foreach (var (key, value) in node)
        {
            var isDefault = string.Equals(key, "DEFAULT", StringComparison.Ordinal);

            if (!isDefault)
            {
                path.Add(key);
            }

            if (value is JsonObject group)
            {
                Walk(group, path, type, set, context);
            }
            else
            {
                var token = new Token { Path = path.ToArray(), Type = type };
                token.Values[context.DefaultMode] = DtcgParser.ReadValue(value);
                set.Tokens.Add(token);
            }

            if (!isDefault)
            {
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    /// <summary>Content of balanced braces starting at the given position.</summary>
    private static string? ExtractBalanced(string content, int openIndex)
    {
        if (openIndex < 0 || openIndex >= content.Length || content[openIndex] != '{')
        {
            return null;
        }

        var depth = 0;

        for (var i = openIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return content[openIndex..(i + 1)];
                }
            }
        }

        return null;
    }

    /// <summary>JS object literal → JSON: quotes keys, removes trailing commas and single quotes.</summary>
    internal static string ToJson(string javascript)
    {
        var text = Regex.Replace(javascript, @"//[^\n]*", string.Empty);
        text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        text = Regex.Replace(text, @"'([^']*)'", "\"$1\"");
        text = Regex.Replace(text, @"([{,]\s*)([A-Za-z_$][A-Za-z0-9_$-]*)\s*:", "$1\"$2\":");
        text = Regex.Replace(text, @",(\s*[}\]])", "$1");

        return text;
    }
}
