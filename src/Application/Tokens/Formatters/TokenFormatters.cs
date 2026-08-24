namespace Snail.MCP.Figma.Application.Tokens.Formatters;

/// <summary>All eleven formatters and lookup by format name.</summary>
public static class TokenFormatters
{
    private static readonly ITokenFormatter[] All =
    [
        new DtcgFormatter(),
        new TokensStudioFormatter(),
        new CssVarsFormatter(),
        new TailwindV4Formatter(),
        new TailwindV3Formatter(),
        new ScssFormatter(),
        new LessFormatter(),
        new TsModuleFormatter(),
        new JsonFlatFormatter(),
        new JsonNestedFormatter(),
        new StyleDictionaryV3Formatter(),
    ];

    public static IReadOnlyList<string> Formats => All.Select(formatter => formatter.Format).ToArray();

    /// <summary>Formatter by format name; <c>null</c> when unknown.</summary>
    public static ITokenFormatter? Find(string? format) =>
        format is null
            ? null
            : All.FirstOrDefault(f => string.Equals(f.Format, format.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Formats that express references natively; others need aliases resolved to final values.</summary>
    public static bool SupportsAliases(string format) =>
        format is "dtcg" or "tokens-studio" or "css-vars" or "scss" or "less"
            or "json-flat" or "json-nested" or "style-dictionary-v3";
}
