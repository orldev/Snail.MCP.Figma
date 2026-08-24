namespace Snail.MCP.Figma.Application.Tokens.Parsers;

/// <summary>Parses a source token file into the shared model, so N parsers + M formatters cost N + M modules instead of N × M.</summary>
public interface ITokenParser
{
    /// <summary>Format name in configuration, e.g. <c>dtcg</c> or <c>css-vars</c>.</summary>
    string Format { get; }

    /// <summary>Parses file content and appends the result to the document.</summary>
    ParseResult Parse(string content, ParseContext context);
}

/// <summary>Where the file came from and where to put the parsed tokens.</summary>
/// <param name="SetName">Set receiving the tokens, usually the file or collection name.</param>
/// <param name="Document">Accumulating document; one parser call may extend what was parsed before.</param>
public sealed record ParseContext(string SetName, TokenDocument Document)
{
    /// <summary>Mode name for formats without modes (CSS, SCSS, flat JSON).</summary>
    public string DefaultMode { get; init; } = "Value";
}

/// <summary>Parse result together with what raised doubts.</summary>
public sealed record ParseResult(TokenDocument Document, IReadOnlyList<string> Warnings)
{
    public int TokenCount => Document.TokenCount;
}
