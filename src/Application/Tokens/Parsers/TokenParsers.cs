namespace Snail.MCP.Figma.Application.Tokens.Parsers;

/// <summary>All nine parsers and lookup by format name or file extension.</summary>
public static class TokenParsers
{
    private static readonly ITokenParser[] All =
    [
        new DtcgParser(),
        new TokensStudioParser(),
        new CssVarsParser(),
        new ScssParser(),
        new TailwindV4Parser(),
        new TailwindV3Parser(),
        new JsonFlatParser(),
        new JsonNestedParser(),
        new StyleDictionaryV3Parser(),
    ];

    public static IReadOnlyList<string> Formats => [.. All.Select(parser => parser.Format)];

    /// <summary>Parser by format name; <c>null</c> when unknown.</summary>
    public static ITokenParser? Find(string? format) =>
        format is null
            ? null
            : All.FirstOrDefault(parser => string.Equals(parser.Format, format.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Format by file name (<c>*.tokens.json</c> → DTCG, <c>*.css</c> → CSS variables…), used when the config names none.</summary>
    public static string GuessFormat(string fileName)
    {
        var name = Path.GetFileName(fileName).ToLowerInvariant();

        if (name.EndsWith(".tokens.json", StringComparison.Ordinal)) return "dtcg";
        if (name is "tailwind.config.js" or "tailwind.config.ts" or "tailwind.config.cjs") return "tailwind-v3";
        if (name.EndsWith(".scss", StringComparison.Ordinal)) return "scss";
        if (name.EndsWith(".css", StringComparison.Ordinal)) return "css-vars";
        if (name.Contains("$themes") || name.Contains("tokens-studio")) return "tokens-studio";

        return "dtcg";
    }
}
