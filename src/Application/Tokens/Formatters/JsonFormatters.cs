namespace Snail.MCP.Figma.Application.Tokens.Formatters;

/// <summary>Flat JSON keyed by dotted path, for consumers that do not know DTCG.</summary>
public sealed class JsonFlatFormatter : ITokenFormatter
{
    public string Format => "json-flat";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var resolver = new AliasResolver(document);
        var root = new JsonObject();
        var mode = TextFormatterCore.PrimaryMode(document);

        foreach (var token in document.AllTokens)
        {
            var value = options.ShouldResolveAliases
                ? resolver.ResolveToLiteral(token, mode)
                : token.ValueFor(mode);

            if (value is null)
            {
                continue;
            }

            root[token.DottedPath] = value.IsReference
                ? JsonValue.Create($"{{{value.Reference}}}")
                : RenderLiteral(value.Literal, token.Type, options);
        }

        warnings.AddRange(resolver.Warnings);

        return new FormatResult(
            [new GeneratedFile($"{options.BaseFileName}.json", root.ToJsonString(DtcgFormatter.Json))],
            warnings);
    }

    internal static JsonNode? RenderLiteral(JsonNode? literal, string type, FormatOptions options)
    {
        return literal switch
        {
            JsonObject or JsonArray => literal.DeepClone(),
            JsonValue value when value.TryGetValue<double>(out var number) &&
                                 type != TokenTypes.Dimension => JsonValue.Create(number),
            _ => JsonValue.Create(ValueRenderer.Render(literal, type, options))
        };
    }
}

/// <summary>Nested JSON without meta keys: groups are objects, values are scalars.</summary>
public sealed class JsonNestedFormatter : ITokenFormatter
{
    public string Format => "json-nested";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var resolver = new AliasResolver(document);
        var root = new JsonObject();
        var mode = TextFormatterCore.PrimaryMode(document);

        foreach (var token in document.AllTokens)
        {
            var value = options.ShouldResolveAliases
                ? resolver.ResolveToLiteral(token, mode)
                : token.ValueFor(mode);

            if (value is null)
            {
                continue;
            }

            var group = root;

            for (var i = 0; i < token.Path.Count - 1; i++)
            {
                if (group[token.Path[i]] is JsonObject existing)
                {
                    group = existing;
                }
                else
                {
                    var created = new JsonObject();
                    group[token.Path[i]] = created;
                    group = created;
                }
            }

            group[token.Path[^1]] = value.IsReference
                ? JsonValue.Create($"{{{value.Reference}}}")
                : JsonFlatFormatter.RenderLiteral(value.Literal, token.Type, options);
        }

        warnings.AddRange(resolver.Warnings);

        return new FormatResult(
            [new GeneratedFile($"{options.BaseFileName}.json", root.ToJsonString(DtcgFormatter.Json))],
            warnings);
    }
}

/// <summary>Style Dictionary v3: <c>value</c> on leaves, references with a <c>.value</c> suffix.</summary>
public sealed class StyleDictionaryV3Formatter : ITokenFormatter
{
    public string Format => "style-dictionary-v3";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var root = new JsonObject();
        var mode = TextFormatterCore.PrimaryMode(document);

        foreach (var token in document.AllTokens)
        {
            var value = token.ValueFor(mode);

            if (value is null)
            {
                continue;
            }

            var group = root;

            foreach (var segment in token.Path.Take(token.Path.Count - 1))
            {
                if (group[segment] is JsonObject existing)
                {
                    group = existing;
                }
                else
                {
                    var created = new JsonObject();
                    group[segment] = created;
                    group = created;
                }
            }

            var leaf = new JsonObject
            {
                ["value"] = value.IsReference
                    ? JsonValue.Create($"{{{value.Reference}.value}}")
                    : JsonFlatFormatter.RenderLiteral(value.Literal, token.Type, options),
                ["type"] = token.Type,
            };

            if (!string.IsNullOrWhiteSpace(token.Description))
            {
                leaf["comment"] = token.Description;
            }

            group[token.Path[^1]] = leaf;
        }

        return new FormatResult(
            [new GeneratedFile($"{options.BaseFileName}.json", root.ToJsonString(DtcgFormatter.Json))],
            warnings);
    }
}

/// <summary>Tokens Studio plugin format: keys without the dollar sign and its own type names.</summary>
public sealed class TokensStudioFormatter : ITokenFormatter
{
    /// <summary>DTCG types → Tokens Studio type names.</summary>
    private static readonly Dictionary<string, string> StudioTypeByDtcgType = new(StringComparer.Ordinal)
    {
        [TokenTypes.Color] = "color",
        [TokenTypes.Dimension] = "sizing",
        [TokenTypes.FontFamily] = "fontFamilies",
        [TokenTypes.FontWeight] = "fontWeights",
        [TokenTypes.Number] = "number",
        [TokenTypes.String] = "text",
        [TokenTypes.Boolean] = "boolean",
        [TokenTypes.Shadow] = "boxShadow",
        [TokenTypes.Typography] = "typography",
        [TokenTypes.Border] = "border",
    };

    public string Format => "tokens-studio";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var root = new JsonObject();
        var multipleSets = document.Sets.Count > 1;

        foreach (var set in document.Sets)
        {
            var target = root;

            if (multipleSets)
            {
                var setGroup = new JsonObject();
                root[set.Name] = setGroup;
                target = setGroup;
            }

            var mode = set.Modes.FirstOrDefault() ?? "Value";

            foreach (var token in set.Tokens)
            {
                var value = token.ValueFor(mode);

                if (value is null)
                {
                    continue;
                }

                var group = target;

                foreach (var segment in token.Path.Take(token.Path.Count - 1))
                {
                    if (group[segment] is JsonObject existing)
                    {
                        group = existing;
                    }
                    else
                    {
                        var created = new JsonObject();
                        group[segment] = created;
                        group = created;
                    }
                }

                var leaf = new JsonObject
                {
                    ["value"] = value.IsReference
                        ? JsonValue.Create($"{{{value.Reference}}}")
                        : JsonFlatFormatter.RenderLiteral(value.Literal, token.Type, options),
                    ["type"] = StudioTypeByDtcgType.GetValueOrDefault(token.Type, token.Type),
                };

                if (!string.IsNullOrWhiteSpace(token.Description))
                {
                    leaf["description"] = token.Description;
                }

                group[token.Path[^1]] = leaf;
            }
        }

        return new FormatResult(
            [new GeneratedFile($"{options.BaseFileName}.json", root.ToJsonString(DtcgFormatter.Json))],
            warnings);
    }
}
