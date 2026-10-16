using System.Text.Json;

namespace Snail.MCP.Figma.Application.Tokens.Parsers;

/// <summary>DTCG (W3C Design Tokens): nested groups with <c>$value</c> and <c>$type</c> on leaves — the canonical storage format.</summary>
public sealed class DtcgParser : ITokenParser
{
    /// <summary>Configuration name of this format, <c>dtcg</c>.</summary>
    public string Format => "dtcg";

    /// <summary>Reads a DTCG JSON file into the context set.</summary>
    public ParseResult Parse(string content, ParseContext context)
    {
        var warnings = new List<string>();

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            return new ParseResult(context.Document, [$"The file did not parse as JSON: {ex.Message}"]);
        }

        if (root is not JsonObject obj)
        {
            return new ParseResult(context.Document, ["A JSON object was expected at the top level."]);
        }

        var set = context.Document.GetOrAddSet(context.SetName);
        Walk(obj, [], inheritedType: null, set, context, warnings);

        if (set.Modes.Count == 0)
        {
            set.Modes.Add(context.DefaultMode);
        }

        return new ParseResult(context.Document, warnings);
    }

    private static void Walk(
        JsonObject node,
        List<string> path,
        string? inheritedType,
        TokenSet set,
        ParseContext context,
        List<string> warnings)
    {
        var groupType = node["$type"]?.GetValue<string>() ?? inheritedType;

        foreach (var (key, value) in node)
        {
            if (key.StartsWith('$'))
            {
                continue;
            }

            if (value is not JsonObject child)
            {
                warnings.Add($"{string.Join('.', path.Append(key))}: an object was expected, skipping.");
                continue;
            }

            path.Add(key);

            if (child.ContainsKey("$value"))
            {
                var token = BuildToken(child, path, groupType, context, warnings);

                if (token is not null)
                {
                    set.Tokens.Add(token);
                    RegisterModes(set, token);
                }
            }
            else
            {
                Walk(child, path, groupType, set, context, warnings);
            }

            path.RemoveAt(path.Count - 1);
        }
    }

    private static Token? BuildToken(
        JsonObject leaf,
        List<string> path,
        string? inheritedType,
        ParseContext context,
        List<string> warnings)
    {
        var type = leaf["$type"]?.GetValue<string>() ?? inheritedType;

        if (type is null)
        {
            type = InferType(leaf["$value"]);
            warnings.Add($"{string.Join('.', path)}: no type given, inferred as {type}.");
        }

        var extensions = leaf["$extensions"] as JsonObject;
        var figma = FigmaTokenExtensions.FromJson(extensions?[FigmaTokenExtensions.Namespace]);

        var token = new Token
        {
            Path = [.. path],
            Type = type,
            Description = leaf["$description"]?.GetValue<string>(),
            Figma = figma,
            ForeignExtensions = BuildForeignExtensions(extensions),
        };

        var primaryMode = figma?.PrimaryMode ?? context.DefaultMode;
        token.Values[primaryMode] = ReadValue(leaf["$value"]);

        if (extensions?[FigmaTokenExtensions.Namespace]?["modes"] is JsonObject modes)
        {
            foreach (var (mode, value) in modes)
            {
                token.Values[mode] = ReadValue(value);
            }
        }

        return token;
    }

    /// <summary>Foreign extensions are carried verbatim; otherwise round trips with Tokens Studio lose data.</summary>
    private static JsonObject? BuildForeignExtensions(JsonObject? extensions)
    {
        if (extensions is null)
        {
            return null;
        }

        var foreign = new JsonObject();

        foreach (var (vendor, value) in extensions)
        {
            if (vendor != FigmaTokenExtensions.Namespace)
            {
                foreign[vendor] = value?.DeepClone();
            }
        }

        return foreign.Count > 0 ? foreign : null;
    }

    internal static TokenValue ReadValue(JsonNode? value)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) &&
            AliasResolver.TryParseReference(text, out var reference))
        {
            return TokenValue.FromReference(reference);
        }

        return TokenValue.FromLiteral(value?.DeepClone());
    }

    /// <summary>Type inferred from the value shape — the last resort when none is declared.</summary>
    internal static string InferType(JsonNode? value) => value switch
    {
        JsonObject obj when obj.ContainsKey("fontFamily") => TokenTypes.Typography,
        JsonObject obj when obj.ContainsKey("offsetX") || obj.ContainsKey("blur") => TokenTypes.Shadow,
        JsonObject obj when obj.ContainsKey("stops") => TokenTypes.Gradient,
        JsonArray => TokenTypes.CubicBezier,
        JsonValue v when v.TryGetValue<bool>(out _) => TokenTypes.Boolean,
        JsonValue v when v.TryGetValue<double>(out _) => TokenTypes.Number,
        JsonValue v when v.TryGetValue<string>(out var text) && text.StartsWith('#') => TokenTypes.Color,
        JsonValue v when v.TryGetValue<string>(out var text) && Transforms.SizeTransform.Parse(text) is not null
            => TokenTypes.Dimension,
        _ => TokenTypes.String,
    };

    internal static void RegisterModes(TokenSet set, Token token)
    {
        foreach (var mode in token.Values.Keys)
        {
            if (!set.Modes.Contains(mode, StringComparer.Ordinal))
            {
                set.Modes.Add(mode);
            }
        }
    }
}
