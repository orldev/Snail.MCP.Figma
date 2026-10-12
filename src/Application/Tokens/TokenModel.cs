using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Tokens;

/// <summary>DTCG token types kept as strings, not an enum: the spec grows and unknown types must pass through.</summary>
public static class TokenTypes
{
    public const string Color = "color";
    public const string Dimension = "dimension";
    public const string FontFamily = "fontFamily";
    public const string FontWeight = "fontWeight";
    public const string Duration = "duration";
    public const string CubicBezier = "cubicBezier";
    public const string Number = "number";
    public const string String = "string";
    public const string Boolean = "boolean";
    public const string Shadow = "shadow";
    public const string Typography = "typography";
    public const string Gradient = "gradient";
    public const string Border = "border";
    public const string Transition = "transition";

    /// <summary>Composite types expand into several CSS properties.</summary>
    public static bool IsComposite(string type) =>
        type is Shadow or Typography or Gradient or Border or Transition;
}

/// <summary>Token value: either a literal or a reference to another token by dotted path, never both.</summary>
public sealed class TokenValue
{
    public JsonNode? Literal { get; init; }

    /// <summary>Dotted path of the target token, e.g. <c>color.primitive.blue.500</c>.</summary>
    public string? Reference { get; init; }

    public bool IsReference => !string.IsNullOrWhiteSpace(Reference);

    public static TokenValue FromLiteral(JsonNode? literal) => new() { Literal = literal };

    public static TokenValue FromLiteral(string literal) => new() { Literal = JsonValue.Create(literal) };

    public static TokenValue FromLiteral(double literal) => new() { Literal = JsonValue.Create(literal) };

    public static TokenValue FromReference(string reference) => new() { Reference = reference };

    /// <summary>Literal as a string; references render as DTCG braces.</summary>
    public string AsText() => IsReference
        ? $"{{{Reference}}}"
        : Literal switch
        {
            null => string.Empty,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => Literal.ToJsonString(),
        };
}

/// <summary>Our metadata inside <c>$extensions</c>, linking the token to its Figma variable.</summary>
public sealed class FigmaTokenExtensions
{
    /// <summary>Namespace key in <c>$extensions</c>, compatible with the donor.</summary>
    public const string Namespace = "figma-console-mcp";

    /// <summary>Figma variable id; survives renames and drives matching.</summary>
    public string? VariableId { get; set; }

    public string? CollectionId { get; set; }

    /// <summary>Native variable type: COLOR, FLOAT, STRING, BOOLEAN.</summary>
    public string? FigmaResolvedType { get; set; }

    /// <summary>Figma variable name when it differs from the token path.</summary>
    public string? FigmaName { get; set; }

    /// <summary>Variable scopes; empty means no opinion and import leaves them alone.</summary>
    public IReadOnlyList<string>? Scopes { get; set; }

    /// <summary>Platform code syntax, e.g. <c>{"WEB":"var(--color-primary)"}</c>.</summary>
    public IReadOnlyDictionary<string, string>? CodeSyntax { get; set; }

    /// <summary>Mode whose value went into the main <c>$value</c>.</summary>
    public string? PrimaryMode { get; set; }

    public JsonObject ToJson()
    {
        var json = new JsonObject();

        if (VariableId is not null) json["variableId"] = VariableId;
        if (CollectionId is not null) json["collectionId"] = CollectionId;
        if (FigmaResolvedType is not null) json["figmaResolvedType"] = FigmaResolvedType;
        if (FigmaName is not null) json["figmaName"] = FigmaName;
        if (PrimaryMode is not null) json["primaryMode"] = PrimaryMode;

        if (Scopes is { Count: > 0 })
        {
            json["scopes"] = new JsonArray(Scopes.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        }

        if (CodeSyntax is { Count: > 0 })
        {
            var syntax = new JsonObject();

            foreach (var (platform, expression) in CodeSyntax)
            {
                syntax[platform] = expression;
            }

            json["codeSyntax"] = syntax;
        }

        return json;
    }

    public static FigmaTokenExtensions? FromJson(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        return new FigmaTokenExtensions
        {
            VariableId = obj["variableId"]?.GetValue<string>(),
            CollectionId = obj["collectionId"]?.GetValue<string>(),
            FigmaResolvedType = obj["figmaResolvedType"]?.GetValue<string>(),
            FigmaName = obj["figmaName"]?.GetValue<string>(),
            PrimaryMode = obj["primaryMode"]?.GetValue<string>(),
            Scopes = (obj["scopes"] as JsonArray)?
                .Select(s => s?.GetValue<string>())
                .Where(s => s is not null)
                .Select(s => s!)
                .ToArray(),
            CodeSyntax = (obj["codeSyntax"] as JsonObject)?
                .ToDictionary(pair => pair.Key, pair => pair.Value?.GetValue<string>() ?? string.Empty),
        };
    }
}

/// <summary>One design token.</summary>
public sealed class Token
{
    /// <summary>Hierarchical path: <c>["color","primary"]</c>.</summary>
    public required IReadOnlyList<string> Path { get; init; }

    public required string Type { get; set; }

    public string? Description { get; set; }

    /// <summary>Values per mode; a single-mode token has one entry.</summary>
    public Dictionary<string, TokenValue> Values { get; init; } = new(StringComparer.Ordinal);

    public FigmaTokenExtensions? Figma { get; set; }

    /// <summary>Other vendors' extensions, carried as-is to keep round trips intact.</summary>
    public JsonObject? ForeignExtensions { get; set; }

    /// <summary>Dotted path — the DTCG reference form.</summary>
    public string DottedPath => string.Join('.', Path);

    /// <summary>Slash path — the Figma variable name form.</summary>
    public string SlashPath => string.Join('/', Path);

    /// <summary>Value for the mode, or the first available one.</summary>
    public TokenValue? ValueFor(string? mode)
    {
        if (mode is not null && Values.TryGetValue(mode, out var value))
        {
            return value;
        }

        return Values.Values.FirstOrDefault();
    }
}

/// <summary>Token set, matching one Figma variable collection.</summary>
public sealed class TokenSet
{
    public required string Name { get; init; }

    public string? Description { get; set; }

    /// <summary>Mode names of the set; a single-mode set usually has one "Value".</summary>
    public List<string> Modes { get; init; } = [];

    public List<Token> Tokens { get; init; } = [];

    /// <summary>Set slug for qualified references and file names.</summary>
    public string Slug => TokenNaming.Slugify(Name);
}

/// <summary>Full token set of a project — what parsers read and formatters write.</summary>
public sealed class TokenDocument
{
    public List<TokenSet> Sets { get; init; } = [];

    public IEnumerable<Token> AllTokens => Sets.SelectMany(set => set.Tokens);

    public int TokenCount => Sets.Sum(set => set.Tokens.Count);

    /// <summary>All modes in the document, in order of appearance.</summary>
    public IReadOnlyList<string> AllModes
    {
        get
        {
            var modes = new List<string>();

            foreach (var mode in Sets.SelectMany(set => set.Modes))
            {
                if (!modes.Contains(mode, StringComparer.Ordinal))
                {
                    modes.Add(mode);
                }
            }

            return modes;
        }
    }

    /// <summary>Set by name, created when missing.</summary>
    public TokenSet GetOrAddSet(string name)
    {
        var existing = Sets.FirstOrDefault(set => string.Equals(set.Name, name, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        var created = new TokenSet { Name = name };
        Sets.Add(created);

        return created;
    }
}

/// <summary>Name normalization for CSS, files and references.</summary>
public static partial class TokenNaming
{
    /// <summary>Lowercase, everything non-alphanumeric becomes a dash.</summary>
    public static string Slugify(string value) =>
        NonSlugCharacterPattern().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');

    /// <summary>Path into a CSS variable name: <c>["color","primary"]</c> → <c>color-primary</c>.</summary>
    public static string ToCssName(IReadOnlyList<string> path) =>
        string.Join('-', path.Select(Slugify));

    /// <summary>camelCase → kebab-case, for composite sub-fields.</summary>
    public static string ToKebab(string value) =>
        CamelHumpPattern().Replace(value, "$1-$2").ToLowerInvariant();

    /// <summary>Path into a JavaScript identifier for ts-module.</summary>
    public static string ToCamel(IReadOnlyList<string> path)
    {
        var parts = path.SelectMany(segment => Slugify(segment).Split('-', StringSplitOptions.RemoveEmptyEntries)).ToArray();

        if (parts.Length == 0)
        {
            return "token";
        }

        var head = parts[0];
        var tail = parts.Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..]);
        var name = head + string.Concat(tail);

        return char.IsDigit(name[0]) ? $"_{name}" : name;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacterPattern();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelHumpPattern();
}
