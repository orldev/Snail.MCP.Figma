using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Tokens.Transforms;

namespace Snail.MCP.Figma.Application.Tokens;

/// <summary>Figma variables into the token model; works for the Variables REST response and plugin data alike.</summary>
public static class FigmaVariableConverter
{
    /// <summary>Native Figma types → DTCG types.</summary>
    private static readonly Dictionary<string, string> TypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["COLOR"] = TokenTypes.Color,
        ["FLOAT"] = TokenTypes.Dimension,
        ["STRING"] = TokenTypes.String,
        ["BOOLEAN"] = TokenTypes.Boolean,
    };

    /// <summary>Builds the document; <paramref name="modeNameMap"/> renames Figma modes to code names.</summary>
    public static TokenDocument Convert(JsonNode variablesData, IReadOnlyDictionary<string, string>? modeNameMap = null)
    {
        var document = new TokenDocument();

        var keyed = VariablesShape.Keyed(variablesData);
        var collections = keyed["variableCollections"]!.AsObject();
        var variables = keyed["variables"]!.AsObject();

        var pathByVariableId = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (variableId, variable) in variables)
        {
            var name = variable?["name"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(name))
            {
                pathByVariableId[variableId] = name.Replace('/', '.');
            }
        }

        var setByCollectionId = new Dictionary<string, TokenSet>(StringComparer.Ordinal);

        foreach (var (collectionId, collection) in collections)
        {
            var set = document.GetOrAddSet(collection?["name"]?.GetValue<string>() ?? collectionId);
            setByCollectionId[collectionId] = set;

            foreach (var mode in (collection?["modes"] as JsonArray) ?? [])
            {
                var modeName = mode?["name"]?.GetValue<string>();

                if (modeName is null)
                {
                    continue;
                }

                var mapped = Rename(modeName, modeNameMap);

                if (!set.Modes.Contains(mapped, StringComparer.Ordinal))
                {
                    set.Modes.Add(mapped);
                }
            }
        }

        foreach (var (variableId, variable) in variables)
        {
            if (variable is null)
            {
                continue;
            }

            var collectionId = variable["variableCollectionId"]?.GetValue<string>();
            var set = collectionId is not null && setByCollectionId.TryGetValue(collectionId, out var found)
                ? found
                : document.GetOrAddSet("Variables");

            var name = variable["name"]?.GetValue<string>() ?? variableId;
            var resolvedType = variable["resolvedType"]?.GetValue<string>() ?? "STRING";

            var token = new Token
            {
                Path = name.Split('/', StringSplitOptions.RemoveEmptyEntries),
                Type = TypeMap.GetValueOrDefault(resolvedType, TokenTypes.String),
                Description = EmptyToNull(variable["description"]?.GetValue<string>()),
                Figma = new FigmaTokenExtensions
                {
                    VariableId = variableId,
                    CollectionId = collectionId,
                    FigmaResolvedType = resolvedType,
                    Scopes = ReadScopes(variable["scopes"]),
                    CodeSyntax = ReadCodeSyntax(variable["codeSyntax"]),
                },
            };

            var modeNames = ModeNamesFor(collections, collectionId, modeNameMap);

            foreach (var (modeId, rawValue) in (variable["valuesByMode"] as JsonObject) ?? [])
            {
                var modeName = modeNames.GetValueOrDefault(modeId, modeId);
                token.Values[modeName] = ConvertValue(rawValue, token.Type, pathByVariableId);
            }

            if (token.Values.Count > 0)
            {
                set.Tokens.Add(token);
            }
        }

        foreach (var set in document.Sets.Where(s => s.Modes.Count == 0))
        {
            set.Modes.Add("Value");
        }

        return document;
    }

    /// <summary>Variable value; an alias to another variable becomes a token reference.</summary>
    internal static TokenValue ConvertValue(
        JsonNode? value,
        string type,
        IReadOnlyDictionary<string, string> pathByVariableId)
    {
        if (value is JsonObject obj)
        {
            if (obj["type"]?.GetValue<string>() == "VARIABLE_ALIAS")
            {
                var targetId = obj["id"]?.GetValue<string>();

                if (targetId is not null && pathByVariableId.TryGetValue(targetId, out var path))
                {
                    return TokenValue.FromReference(path);
                }

                return TokenValue.FromLiteral(JsonValue.Create($"{{unresolved:{targetId}}}"));
            }

            if (type == TokenTypes.Color && obj.ContainsKey("r"))
            {
                var color = new ColorTransform.Rgba(
                    obj["r"]?.GetValue<double>() ?? 0,
                    obj["g"]?.GetValue<double>() ?? 0,
                    obj["b"]?.GetValue<double>() ?? 0,
                    obj["a"]?.GetValue<double>() ?? 1);

                return TokenValue.FromLiteral(ColorTransform.Format(color, "hex"));
            }

            return TokenValue.FromLiteral(obj.DeepClone());
        }

        if (type == TokenTypes.Dimension && value is JsonValue number && number.TryGetValue<double>(out var pixels))
        {
            return TokenValue.FromLiteral(SizeTransform.Render(pixels, "px"));
        }

        return TokenValue.FromLiteral(value?.DeepClone());
    }

    private static Dictionary<string, string> ModeNamesFor(
        JsonObject collections,
        string? collectionId,
        IReadOnlyDictionary<string, string>? modeNameMap)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (collectionId is null || collections[collectionId] is not JsonObject collection)
        {
            return result;
        }

        foreach (var mode in (collection["modes"] as JsonArray) ?? [])
        {
            var modeId = mode?["modeId"]?.GetValue<string>();
            var modeName = mode?["name"]?.GetValue<string>();

            if (modeId is not null && modeName is not null)
            {
                result[modeId] = Rename(modeName, modeNameMap);
            }
        }

        return result;
    }

    private static string Rename(string mode, IReadOnlyDictionary<string, string>? map) =>
        map is not null && map.TryGetValue(mode, out var mapped) ? mapped : mode;

    private static IReadOnlyList<string>? ReadScopes(JsonNode? scopes)
    {
        var values = (scopes as JsonArray)?
            .Select(scope => scope?.GetValue<string>())
            .Where(scope => scope is not null)
            .Select(scope => scope!)
            .ToArray();

        if (values is null || values.Length == 0 || (values.Length == 1 && values[0] == "ALL_SCOPES"))
        {
            return null;
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string>? ReadCodeSyntax(JsonNode? codeSyntax)
    {
        if (codeSyntax is not JsonObject obj || obj.Count == 0)
        {
            return null;
        }

        return obj.ToDictionary(pair => pair.Key, pair => pair.Value?.GetValue<string>() ?? string.Empty);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
