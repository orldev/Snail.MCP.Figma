using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Tokens;

/// <summary>Parses and resolves references between tokens: <c>{color.primary}</c> in DTCG, slash form in Tokens Studio, dotted paths in Style Dictionary.</summary>
public sealed partial class AliasResolver
{
    /// <summary>Whole-string references only; partial substitution is not supported.</summary>
    [GeneratedRegex(@"^\{([^{}]+)\}$")]
    private static partial Regex ReferencePattern();

    private readonly Dictionary<string, Token> _byDottedPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Token> _bySetQualifiedPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings = [];

    public AliasResolver(TokenDocument document)
    {
        foreach (var set in document.Sets)
        {
            foreach (var token in set.Tokens)
            {
                _byDottedPath.TryAdd(token.DottedPath, token);
                _bySetQualifiedPath[$"{set.Slug}.{token.DottedPath}"] = token;
            }
        }
    }

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>When the string is a reference, returns the path without braces.</summary>
    public static bool TryParseReference(string? text, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = ReferencePattern().Match(text.Trim());

        if (!match.Success)
        {
            return false;
        }

        path = match.Groups[1].Value.Trim().Replace('/', '.');

        return true;
    }

    /// <summary>Token the reference points to; <c>null</c> when not found.</summary>
    public Token? Resolve(string reference)
    {
        var normalized = reference.Replace('/', '.');

        if (_byDottedPath.TryGetValue(normalized, out var token))
        {
            return token;
        }

        return _bySetQualifiedPath.TryGetValue(normalized, out var qualified) ? qualified : null;
    }

    /// <summary>Follows the reference chain to a literal; cycles stop with a warning.</summary>
    public TokenValue? ResolveToLiteral(Token token, string? mode, int maxDepth = 16)
    {
        var current = token;
        var value = current.ValueFor(mode);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current.DottedPath };

        for (var depth = 0; value is { IsReference: true } && depth < maxDepth; depth++)
        {
            var target = Resolve(value.Reference!);

            if (target is null)
            {
                _warnings.Add($"Token {current.DottedPath}: the reference {{{value.Reference}}} leads nowhere.");

                return null;
            }

            if (!visited.Add(target.DottedPath))
            {
                _warnings.Add($"Token {token.DottedPath}: a reference loop was found through {target.DottedPath}.");

                return null;
            }

            current = target;
            value = current.ValueFor(mode) ?? current.ValueFor(null);
        }

        if (value is { IsReference: true })
        {
            _warnings.Add($"Token {token.DottedPath}: the reference chain is deeper than {maxDepth} — not unfolding it.");

            return null;
        }

        return value;
    }

    /// <summary>Target token path — needed to reference it in CSS through <c>var(--…)</c>.</summary>
    public IReadOnlyList<string>? ReferenceTargetPath(string reference) => Resolve(reference)?.Path;
}
