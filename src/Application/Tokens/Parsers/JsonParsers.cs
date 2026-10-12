using System.Text.Json;

namespace Snail.MCP.Figma.Application.Tokens.Parsers;

/// <summary>Flat JSON: dotted path key, literal value.</summary>
public sealed class JsonFlatParser : ITokenParser
{
    public string Format => "json-flat";

    public ParseResult Parse(string content, ParseContext context)
    {
        var warnings = new List<string>();

        if (!TryParseObject(content, out var root, out var error))
        {
            return new ParseResult(context.Document, [error!]);
        }

        var set = context.Document.GetOrAddSet(context.SetName);

        foreach (var (key, value) in root!)
        {
            var path = key.Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries);

            if (path.Length == 0)
            {
                continue;
            }

            var token = new Token { Path = path, Type = DtcgParser.InferType(value) };
            token.Values[context.DefaultMode] = DtcgParser.ReadValue(value);
            set.Tokens.Add(token);
        }

        if (set.Modes.Count == 0)
        {
            set.Modes.Add(context.DefaultMode);
        }

        return new ParseResult(context.Document, warnings);
    }

    internal static bool TryParseObject(string content, out JsonObject? root, out string? error)
    {
        root = null;
        error = null;

        try
        {
            if (JsonNode.Parse(content) is JsonObject parsed)
            {
                root = parsed;

                return true;
            }

            error = "A JSON object was expected at the top level.";

            return false;
        }
        catch (JsonException ex)
        {
            error = $"The file did not parse as JSON: {ex.Message}";

            return false;
        }
    }
}

/// <summary>Nested JSON without DTCG meta keys: groups are objects, leaves are scalars.</summary>
public sealed class JsonNestedParser : ITokenParser
{
    public string Format => "json-nested";

    public ParseResult Parse(string content, ParseContext context)
    {
        if (!JsonFlatParser.TryParseObject(content, out var root, out var error))
        {
            return new ParseResult(context.Document, [error!]);
        }

        var set = context.Document.GetOrAddSet(context.SetName);
        Walk(root!, [], set, context);

        if (set.Modes.Count == 0)
        {
            set.Modes.Add(context.DefaultMode);
        }

        return new ParseResult(context.Document, []);
    }

    private static void Walk(JsonObject node, List<string> path, TokenSet set, ParseContext context)
    {
        foreach (var (key, value) in node)
        {
            path.Add(key);

            if (value is JsonObject group)
            {
                Walk(group, path, set, context);
            }
            else
            {
                var token = new Token { Path = [.. path], Type = DtcgParser.InferType(value) };
                token.Values[context.DefaultMode] = DtcgParser.ReadValue(value);
                set.Tokens.Add(token);
            }

            path.RemoveAt(path.Count - 1);
        }
    }
}

/// <summary>Style Dictionary v3: nested groups, <c>value</c> on leaves, braced references with a <c>.value</c> suffix that is dropped.</summary>
public sealed class StyleDictionaryV3Parser : ITokenParser
{
    public string Format => "style-dictionary-v3";

    public ParseResult Parse(string content, ParseContext context)
    {
        if (!JsonFlatParser.TryParseObject(content, out var root, out var error))
        {
            return new ParseResult(context.Document, [error!]);
        }

        var set = context.Document.GetOrAddSet(context.SetName);
        Walk(root!, [], set, context);

        if (set.Modes.Count == 0)
        {
            set.Modes.Add(context.DefaultMode);
        }

        return new ParseResult(context.Document, []);
    }

    private static void Walk(JsonObject node, List<string> path, TokenSet set, ParseContext context)
    {
        foreach (var (key, value) in node)
        {
            if (value is not JsonObject child)
            {
                continue;
            }

            path.Add(key);

            if (child.ContainsKey("value"))
            {
                var token = new Token
                {
                    Path = [.. path],
                    Type = child["type"]?.GetValue<string>() ?? DtcgParser.InferType(child["value"]),
                    Description = child["comment"]?.GetValue<string>(),
                };

                token.Values[context.DefaultMode] = ReadValue(child["value"]);
                set.Tokens.Add(token);
            }
            else
            {
                Walk(child, path, set, context);
            }

            path.RemoveAt(path.Count - 1);
        }
    }

    private static TokenValue ReadValue(JsonNode? value)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) &&
            AliasResolver.TryParseReference(text, out var reference))
        {
            if (reference.EndsWith(".value", StringComparison.Ordinal))
            {
                reference = reference[..^".value".Length];
            }

            return TokenValue.FromReference(reference);
        }

        return TokenValue.FromLiteral(value?.DeepClone());
    }
}
