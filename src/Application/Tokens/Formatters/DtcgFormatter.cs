using System.Text.Encodings.Web;
using System.Text.Json;

namespace Snail.MCP.Figma.Application.Tokens.Formatters;

/// <summary>Canonical DTCG: nested groups, <c>$value</c>/<c>$type</c>/<c>$description</c> on leaves, extra modes in <c>$extensions</c>.</summary>
public sealed class DtcgFormatter : ITokenFormatter
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A leaf named like a group moves under this key: a JSON object cannot hold both a value and children.</summary>
    private const string LeafRemapKey = "@";

    /// <summary>Configuration name of this format, <c>dtcg</c>.</summary>
    public string Format => "dtcg";

    /// <summary>Renders the document as DTCG <c>.tokens.json</c> files.</summary>
    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var files = new List<GeneratedFile>();

        if (options.ShouldSplitByCollection)
        {
            foreach (var set in document.Sets)
            {
                files.Add(new GeneratedFile(
                    $"{options.BaseFileName}-{set.Slug}.tokens.json",
                    Render([set], options, warnings)));
            }
        }
        else
        {
            files.Add(new GeneratedFile(
                $"{options.BaseFileName}.tokens.json",
                Render(document.Sets, options, warnings)));
        }

        return new FormatResult(files, warnings);
    }

    private static string Render(IReadOnlyCollection<TokenSet> sets, FormatOptions options, List<string> warnings)
    {
        var root = new JsonObject();

        var qualifier = sets.Count > 1 ? BuildSetQualifier(sets) : null;

        foreach (var set in sets)
        {
            var target = sets.Count > 1 ? GetOrAddGroup(root, set.Name) : root;

            foreach (var token in set.Tokens)
            {
                Place(target, token, set, options, warnings, qualifier);
            }
        }

        return root.ToJsonString(Json);
    }

    /// <summary>Token path → set name it lives under; the first set wins.</summary>
    private static Dictionary<string, string> BuildSetQualifier(IEnumerable<TokenSet> sets)
    {
        var qualifier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in sets)
        {
            foreach (var token in set.Tokens)
            {
                qualifier.TryAdd(token.DottedPath, set.Name);
            }
        }

        return qualifier;
    }

    /// <summary>Prefixes the reference with the set name when the target lives under another root key.</summary>
    private static string QualifyReference(string reference, Dictionary<string, string>? qualifier)
    {
        if (qualifier is null)
        {
            return reference;
        }

        var normalized = reference.Replace('/', '.');

        return qualifier.TryGetValue(normalized, out var setName) ? $"{setName}.{normalized}" : reference;
    }

    private static void Place(
        JsonObject root,
        Token token,
        TokenSet set,
        FormatOptions options,
        List<string> warnings,
        Dictionary<string, string>? qualifier)
    {
        var group = root;

        for (var i = 0; i < token.Path.Count - 1; i++)
        {
            group = GetOrAddGroup(group, token.Path[i]);
        }

        var leafName = token.Path[^1];
        var leaf = BuildLeaf(token, set, options, warnings, qualifier);

        if (group[leafName] is JsonObject existing && !existing.ContainsKey("$value"))
        {
            existing[LeafRemapKey] = leaf;

            return;
        }

        group[leafName] = leaf;
    }

    private static JsonObject BuildLeaf(
        Token token,
        TokenSet set,
        FormatOptions options,
        List<string> warnings,
        Dictionary<string, string>? qualifier)
    {
        var primaryMode = token.Figma?.PrimaryMode
                          ?? set.Modes.FirstOrDefault(mode => token.Values.ContainsKey(mode))
                          ?? token.Values.Keys.FirstOrDefault();

        var leaf = new JsonObject { ["$type"] = token.Type };

        var value = primaryMode is null ? null : token.ValueFor(primaryMode);

        if (value is null)
        {
            warnings.Add($"{token.DottedPath}: no value, writing null.");
            leaf["$value"] = null;
        }
        else
        {
            leaf["$value"] = RenderValue(value, token, options, qualifier);
        }

        if (!string.IsNullOrWhiteSpace(token.Description))
        {
            leaf["$description"] = token.Description;
        }

        var extensions = BuildExtensions(token, primaryMode, options, qualifier);

        if (extensions is not null)
        {
            leaf["$extensions"] = extensions;
        }

        return leaf;
    }

    private static JsonNode? RenderValue(
        TokenValue value,
        Token token,
        FormatOptions options,
        Dictionary<string, string>? qualifier = null)
    {
        if (value.IsReference)
        {
            return JsonValue.Create($"{{{QualifyReference(value.Reference!, qualifier)}}}");
        }

        if (TokenTypes.IsComposite(token.Type) || value.Literal is JsonObject or JsonArray)
        {
            return value.Literal?.DeepClone();
        }

        var rendered = ValueRenderer.Render(value.Literal, token.Type, options);

        return value.Literal is JsonValue literal && literal.TryGetValue<double>(out var number) &&
               token.Type != TokenTypes.Dimension
            ? JsonValue.Create(number)
            : JsonValue.Create(rendered);
    }

    private static JsonObject? BuildExtensions(
        Token token,
        string? primaryMode,
        FormatOptions options,
        Dictionary<string, string>? qualifier)
    {
        var ours = token.Figma?.ToJson() ?? [];

        if (primaryMode is not null && token.Values.Count > 1)
        {
            ours["primaryMode"] = primaryMode;

            var modes = new JsonObject();

            foreach (var (mode, value) in token.Values)
            {
                if (mode == primaryMode)
                {
                    continue;
                }

                modes[mode] = RenderValue(value, token, options, qualifier);
            }

            ours["modes"] = modes;
        }

        var extensions = new JsonObject();

        if (token.ForeignExtensions is not null)
        {
            foreach (var (vendor, value) in token.ForeignExtensions)
            {
                extensions[vendor] = value?.DeepClone();
            }
        }

        if (ours.Count > 0)
        {
            extensions[FigmaTokenExtensions.Namespace] = ours;
        }

        return extensions.Count > 0 ? extensions : null;
    }

    private static JsonObject GetOrAddGroup(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[name] = created;

        return created;
    }
}
