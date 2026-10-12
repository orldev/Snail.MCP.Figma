using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Tokens.Parsers;

/// <summary>SCSS variables such as <c>$color-primary: #FF0000;</c> and nested Sass maps.</summary>
public sealed partial class ScssParser : ITokenParser
{
    [GeneratedRegex(@"^\s*\$(?<name>[A-Za-z0-9_-]+)\s*:\s*(?<value>[^;]+);", RegexOptions.Multiline)]
    private static partial Regex VariablePattern();

    [GeneratedRegex(@"^\$(?<name>[A-Za-z0-9_-]+)$")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineCommentPattern();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentPattern();

    public string Format => "scss";

    public ParseResult Parse(string content, ParseContext context)
    {
        var warnings = new List<string>();
        var set = context.Document.GetOrAddSet(context.SetName);

        var stripped = LineCommentPattern().Replace(content, string.Empty);
        stripped = BlockCommentPattern().Replace(stripped, string.Empty);

        foreach (Match match in VariablePattern().Matches(stripped))
        {
            var name = match.Groups["name"].Value;
            var rawValue = match.Groups["value"].Value.Trim();

            if (rawValue.StartsWith('('))
            {
                warnings.Add($"${name}: Sass maps are not supported, skipping.");
                continue;
            }

            var path = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
            var token = new Token { Path = path, Type = InferType(rawValue) };

            var reference = ReferencePattern().Match(rawValue);
            token.Values[context.DefaultMode] = reference.Success
                ? TokenValue.FromReference(reference.Groups["name"].Value.Replace('-', '.'))
                : TokenValue.FromLiteral(rawValue);

            set.Tokens.Add(token);
        }

        if (set.Modes.Count == 0)
        {
            set.Modes.Add(context.DefaultMode);
        }

        if (set.Tokens.Count == 0)
        {
            warnings.Add("The file declares no $variable at all.");
        }

        return new ParseResult(context.Document, warnings);
    }

    private static string InferType(string value)
    {
        if (value.StartsWith('#') || value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TokenTypes.Color;
        }

        return Transforms.SizeTransform.Parse(value) is not null && value.Any(char.IsLetter)
            ? TokenTypes.Dimension
            : double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _)
                ? TokenTypes.Number
                : TokenTypes.String;
    }
}
