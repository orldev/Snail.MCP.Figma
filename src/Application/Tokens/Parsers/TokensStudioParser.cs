using System.Text.Json;

namespace Snail.MCP.Figma.Application.Tokens.Parsers;

/// <summary>Tokens Studio plugin format: nested groups with unprefixed keys and token sets at the top level.</summary>
public sealed class TokensStudioParser : ITokenParser
{
    /// <summary>Tokens Studio meta keys that are not sets.</summary>
    private static readonly HashSet<string> ReservedTopLevel = new(StringComparer.Ordinal)
    {
        "$themes", "$metadata", "$figmaStyleReferences", "$figmaVariableReferences",
    };

    /// <summary>Tokens Studio type names → DTCG types.</summary>
    private static readonly Dictionary<string, string> DtcgTypeByStudioType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["color"] = TokenTypes.Color,
        ["sizing"] = TokenTypes.Dimension,
        ["spacing"] = TokenTypes.Dimension,
        ["borderRadius"] = TokenTypes.Dimension,
        ["borderWidth"] = TokenTypes.Dimension,
        ["opacity"] = TokenTypes.Number,
        ["fontFamilies"] = TokenTypes.FontFamily,
        ["fontWeights"] = TokenTypes.FontWeight,
        ["fontSizes"] = TokenTypes.Dimension,
        ["lineHeights"] = TokenTypes.Dimension,
        ["letterSpacing"] = TokenTypes.Dimension,
        ["typography"] = TokenTypes.Typography,
        ["boxShadow"] = TokenTypes.Shadow,
        ["border"] = TokenTypes.Border,
        ["dimension"] = TokenTypes.Dimension,
        ["number"] = TokenTypes.Number,
        ["text"] = TokenTypes.String,
        ["boolean"] = TokenTypes.Boolean,
    };

    /// <summary>Configuration name of this format, <c>tokens-studio</c>.</summary>
    public string Format => "tokens-studio";

    /// <summary>Reads a Tokens Studio JSON file into the document, one set per name in <c>$metadata.tokenSetOrder</c> when it lists any.</summary>
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

        var declaredSets = (obj["$metadata"]?["tokenSetOrder"] as JsonArray)?
            .Select(name => name?.GetValue<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray() ?? [];

        if (declaredSets.Length > 0)
        {
            foreach (var setName in declaredSets)
            {
                if (obj[setName] is not JsonObject setNode)
                {
                    warnings.Add($"$metadata promises the set '{setName}', but the file does not have it.");
                    continue;
                }

                var set = context.Document.GetOrAddSet(setName);
                Walk(setNode, [], null, set, context, warnings);
                EnsureMode(set, context);
            }
        }
        else
        {
            var set = context.Document.GetOrAddSet(context.SetName);
            Walk(obj, [], null, set, context, warnings);
            EnsureMode(set, context);
        }

        return new ParseResult(context.Document, warnings);
    }

    private static void EnsureMode(TokenSet set, ParseContext context)
    {
        if (set.Modes.Count == 0)
        {
            set.Modes.Add(context.DefaultMode);
        }
    }

    private static void Walk(
        JsonObject node,
        List<string> path,
        string? inheritedType,
        TokenSet set,
        ParseContext context,
        List<string> warnings)
    {
        var groupType = node["type"]?.GetValue<string>() ?? inheritedType;

        foreach (var (key, value) in node)
        {
            if (key.StartsWith('$') || key is "type" or "value" or "description")
            {
                continue;
            }

            if (value is not JsonObject child)
            {
                continue;
            }

            path.Add(key);

            if (child.ContainsKey("value"))
            {
                var rawType = child["type"]?.GetValue<string>() ?? groupType;
                var type = rawType is not null && DtcgTypeByStudioType.TryGetValue(rawType, out var mapped)
                    ? mapped
                    : rawType ?? DtcgParser.InferType(child["value"]);

                var token = new Token
                {
                    Path = [.. path],
                    Type = type,
                    Description = child["description"]?.GetValue<string>(),
                    ForeignExtensions = child["$extensions"] is JsonObject ext ? (JsonObject)ext.DeepClone() : null,
                };

                token.Values[context.DefaultMode] = DtcgParser.ReadValue(child["value"]);
                set.Tokens.Add(token);
                DtcgParser.RegisterModes(set, token);
            }
            else
            {
                Walk(child, path, groupType, set, context, warnings);
            }

            path.RemoveAt(path.Count - 1);
        }
    }
}
