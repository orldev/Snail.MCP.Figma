using System.Globalization;

namespace Snail.MCP.Figma.Application.Extraction;

/// <summary>Builds the design-system kit: tokens, components and styles in one code-generation-ready response, kept compact by visual specs, delta-encoded variants and tiered compression.</summary>
public static class DesignSystemKit
{
    /// <summary>Compression levels in ascending aggressiveness.</summary>
    public const string Summary = "summary";
    /// <summary>The middle level, which keeps components to their names, descriptions and properties and trims the token variables.</summary>
    public const string Inventory = "inventory";
    /// <summary>The most aggressive level, which drops the token collections and keeps components to names, ids and trimmed properties.</summary>
    public const string Compact = "compact";

    /// <summary>Figma color (0..1 components) into uppercase hex, with an alpha byte when the color is not opaque.</summary>
    /// <remarks>An effect keeps its opacity in the color alone: without the alpha byte a 25 % shadow came out solid black.</remarks>
    public static string RgbaToHex(JsonNode? color)
    {
        if (color is null)
        {
            return "#000000";
        }

        static int Channel(JsonNode? value) =>
            (int)Math.Clamp(Math.Round((value?.GetValue<double>() ?? 0) * 255, MidpointRounding.AwayFromZero), 0, 255);

        var opaque = $"#{Channel(color["r"]):X2}{Channel(color["g"]):X2}{Channel(color["b"]):X2}";
        var alpha = color["a"] is null ? 255 : Channel(color["a"]);

        return alpha == 255 ? opaque : $"{opaque}{alpha:X2}";
    }

    /// <summary>Compact visual spec of a node — what it takes to reproduce it in CSS; <c>null</c> when there is nothing to reproduce.</summary>
    public static JsonObject? ExtractVisualSpec(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var spec = new JsonObject();
        var hasData = false;

        if (node["fills"] is JsonArray fills && fills.Count > 0)
        {
            var mapped = new JsonArray(fills
                .Where(fill => fill?["visible"]?.GetValue<bool>() != false)
                .Select(fill =>
                {
                    var entry = new JsonObject { ["type"] = fill?["type"]?.DeepClone() };

                    if (fill?["color"] is { } color) entry["color"] = RgbaToHex(color);
                    if (fill?["opacity"] is { } opacity) entry["opacity"] = opacity.DeepClone();

                    return (JsonNode)entry;
                })
                .ToArray());

            if (mapped.Count > 0)
            {
                spec["fills"] = mapped;
                hasData = true;
            }
        }

        if (node["strokes"] is JsonArray strokes && strokes.Count > 0)
        {
            var mapped = new JsonArray(strokes
                .Where(stroke => stroke?["visible"]?.GetValue<bool>() != false)
                .Select(stroke =>
                {
                    var entry = new JsonObject { ["type"] = stroke?["type"]?.DeepClone() };

                    if (stroke?["color"] is { } color) entry["color"] = RgbaToHex(color);
                    if (node["strokeWeight"] is { } weight) entry["weight"] = weight.DeepClone();
                    if (node["strokeAlign"] is { } align) entry["align"] = align.DeepClone();

                    return (JsonNode)entry;
                })
                .ToArray());

            if (mapped.Count > 0)
            {
                spec["strokes"] = mapped;
                hasData = true;
            }
        }

        if (node["effects"] is JsonArray effects && effects.Count > 0)
        {
            var mapped = new JsonArray(effects
                .Where(effect => effect?["visible"]?.GetValue<bool>() != false)
                .Select(effect =>
                {
                    var entry = new JsonObject { ["type"] = effect?["type"]?.DeepClone() };

                    if (effect?["color"] is { } color) entry["color"] = RgbaToHex(color);
                    if (effect?["offset"] is { } offset) entry["offset"] = offset.DeepClone();
                    if (effect?["radius"] is { } radius) entry["radius"] = radius.DeepClone();
                    if (effect?["spread"] is { } spread) entry["spread"] = spread.DeepClone();

                    return (JsonNode)entry;
                })
                .ToArray());

            if (mapped.Count > 0)
            {
                spec["effects"] = mapped;
                hasData = true;
            }
        }

        if (node["cornerRadius"]?.GetValue<double>() is > 0 and var radiusValue)
        {
            spec["cornerRadius"] = radiusValue;
            hasData = true;
        }

        if (node["rectangleCornerRadii"] is { } radii)
        {
            spec["rectangleCornerRadii"] = radii.DeepClone();
            hasData = true;
        }

        if (node["opacity"]?.GetValue<double>() is < 1 and var opacityValue)
        {
            spec["opacity"] = opacityValue;
            hasData = true;
        }

        var layoutMode = node["layoutMode"]?.GetValue<string>();

        if (!string.IsNullOrEmpty(layoutMode) && layoutMode != "NONE")
        {
            var layout = new JsonObject { ["mode"] = layoutMode };

            foreach (var (source, target) in new[]
                     {
                         ("paddingTop", "paddingTop"), ("paddingRight", "paddingRight"),
                         ("paddingBottom", "paddingBottom"), ("paddingLeft", "paddingLeft"),
                         ("itemSpacing", "itemSpacing"),
                         ("primaryAxisAlignItems", "primaryAxisAlign"),
                         ("counterAxisAlignItems", "counterAxisAlign"),
                     })
            {
                if (node[source] is { } value)
                {
                    layout[target] = value.DeepClone();
                }
            }

            spec["layout"] = layout;
            hasData = true;
        }

        if (node["type"]?.GetValue<string>() == "TEXT" && node["style"] is JsonObject style)
        {
            var typography = new JsonObject();

            foreach (var (source, target) in new[]
                     {
                         ("fontFamily", "fontFamily"), ("fontSize", "fontSize"), ("fontWeight", "fontWeight"),
                         ("lineHeightPx", "lineHeight"), ("letterSpacing", "letterSpacing"),
                         ("textAlignHorizontal", "textAlignHorizontal"),
                     })
            {
                if (style[source] is { } value)
                {
                    typography[target] = value.DeepClone();
                }
            }

            spec["typography"] = typography;
            hasData = true;
        }

        return hasData ? spec : null;
    }

    /// <summary>Delta-encodes variant visual specs against the first variant that has one; identical variants carry neither field.</summary>
    public static void DeltaEncodeVariants(JsonArray variants)
    {
        var baseVariant = variants.FirstOrDefault(variant => variant?["visualSpec"] is JsonObject);

        if (baseVariant?["visualSpec"] is not JsonObject baseSpec)
        {
            return;
        }

        foreach (var variant in variants)
        {
            if (ReferenceEquals(variant, baseVariant) || variant?["visualSpec"] is not JsonObject spec)
            {
                continue;
            }

            var delta = new JsonObject();
            var keys = baseSpec.Select(pair => pair.Key)
                .Concat(spec.Select(pair => pair.Key))
                .Distinct(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                var inSpec = spec.ContainsKey(key);
                var inBase = baseSpec.ContainsKey(key);

                if (!inSpec)
                {
                    delta[key] = null;
                }
                else if (!inBase || !AreJsonEqual(spec[key], baseSpec[key]))
                {
                    delta[key] = spec[key]?.DeepClone();
                }
            }

            variant.AsObject().Remove("visualSpec");

            if (delta.Count > 0)
            {
                variant["visualSpecDelta"] = delta;
            }
        }
    }

    private static bool AreJsonEqual(JsonNode? left, JsonNode? right) =>
        string.Equals(left?.ToJsonString(), right?.ToJsonString(), StringComparison.Ordinal);

    /// <summary>Compression level by format and payload size, tuned to consumer model context windows.</summary>
    public static string? CompressionLevel(string format, double sizeKb)
    {
        var level = format switch
        {
            Compact => Compact,
            Summary => Summary,
            _ => null,
        };

        if (sizeKb > 500)
        {
            return Compact;
        }

        if (sizeKb > 200)
        {
            return level is null or Summary ? Inventory : level;
        }

        if (sizeKb > 100)
        {
            return level ?? Summary;
        }

        return level;
    }

    /// <summary>Shrinks the assembled kit to the given level.</summary>
    public static JsonObject Compress(JsonObject kit, string level)
    {
        var compressed = (JsonObject)kit.DeepClone();

        CompressTokens(compressed, level);
        CompressComponents(compressed, level);
        CompressStyles(compressed, level);

        return compressed;
    }

    private static void CompressTokens(JsonObject kit, string level)
    {
        if (kit["tokens"] is not JsonObject tokens)
        {
            return;
        }

        if (level == Compact)
        {
            tokens["collections"] = new JsonArray();

            return;
        }

        if (level != Inventory || tokens["collections"] is not JsonArray collections)
        {
            return;
        }

        foreach (var collection in collections)
        {
            if (collection?["variables"] is not JsonArray variables)
            {
                continue;
            }

            foreach (var variable in variables)
            {
                variable?.AsObject().Remove("valuesByMode");
            }
        }
    }

    private static void CompressComponents(JsonObject kit, string level)
    {
        if (kit["components"] is not JsonObject components || components["items"] is not JsonArray items)
        {
            return;
        }

        if (level == Compact)
        {
            var sets = items.Where(item => item?["variants"] is JsonArray { Count: > 0 }).ToList();
            var standalone = items.Where(item => item?["variants"] is not JsonArray { Count: > 0 }).ToList();
            var kept = sets.Concat(standalone.Take(100)).ToList();

            var trimmed = new JsonArray(kept.Select(item =>
            {
                var entry = new JsonObject
                {
                    ["id"] = item?["id"]?.DeepClone(),
                    ["name"] = item?["name"]?.DeepClone(),
                };

                if (item?["variants"] is JsonArray variants)
                {
                    entry["variants"] = variants.Count > 10
                        ? new JsonArray(new JsonObject { ["name"] = $"{variants.Count} variants", ["id"] = string.Empty })
                        : [.. variants.Select(variant => (JsonNode)new JsonObject
                        {
                            ["name"] = variant?["name"]?.DeepClone(),
                            ["id"] = variant?["id"]?.DeepClone(),
                        })];
                }

                if (item?["properties"] is JsonObject properties)
                {
                    entry["properties"] = TrimProperties(properties);
                }

                return (JsonNode)entry;
            }).ToArray());

            components["items"] = trimmed;

            if (components["summary"] is JsonObject summary)
            {
                summary["totalComponents"] = trimmed.Count;

                if (standalone.Count > 100)
                {
                    summary["omittedStandaloneComponents"] = standalone.Count - 100;
                }
            }
        }
        else if (level == Inventory)
        {
            components["items"] = new JsonArray(items.Select(item =>
            {
                var entry = new JsonObject
                {
                    ["id"] = item?["id"]?.DeepClone(),
                    ["name"] = item?["name"]?.DeepClone(),
                    ["description"] = item?["description"]?.DeepClone(),
                };

                if (item?["properties"] is JsonObject properties)
                {
                    entry["properties"] = TrimProperties(properties);
                }

                return (JsonNode)entry;
            }).ToArray());
        }
        else if (level == Summary)
        {
            foreach (var item in items)
            {
                if (item?["variants"] is not JsonArray variants)
                {
                    continue;
                }

                item["variants"] = new JsonArray(variants.Select(variant => (JsonNode)new JsonObject
                {
                    ["name"] = variant?["name"]?.DeepClone(),
                    ["id"] = variant?["id"]?.DeepClone(),
                }).ToArray());
            }
        }

        if (components["items"] is JsonArray finalItems)
        {
            foreach (var item in finalItems)
            {
                item?.AsObject().Remove("imageUrl");
            }
        }
    }

    private static JsonObject TrimProperties(JsonObject properties)
    {
        var trimmed = new JsonObject();

        foreach (var (name, definition) in properties)
        {
            trimmed[name] = new JsonObject
            {
                ["type"] = definition?["type"]?.DeepClone(),
                ["defaultValue"] = definition?["defaultValue"]?.DeepClone(),
            };
        }

        return trimmed;
    }

    private static void CompressStyles(JsonObject kit, string level)
    {
        if (kit["styles"] is not JsonObject styles || styles["items"] is not JsonArray items)
        {
            return;
        }

        if (level == Compact)
        {
            styles["items"] = new JsonArray(items.Select(style => (JsonNode)new JsonObject
            {
                ["key"] = style?["key"]?.DeepClone(),
                ["name"] = style?["name"]?.DeepClone(),
                ["styleType"] = style?["styleType"]?.DeepClone(),
            }).ToArray());
        }
        else if (level == Inventory)
        {
            foreach (var style in items)
            {
                style?.AsObject().Remove("resolvedValue");
            }
        }
    }

    /// <summary>Instruction for the model: what the data holds and how it maps to CSS.</summary>
    public static string AiInstruction(IReadOnlyList<string> sections) =>
        "DESIGN SYSTEM SPECIFICATION — STRICT VISUAL ACCURACY REQUIRED\n\n" +
        $"What is inside: {string.Join(", ", sections)}.\n\n" +
        "RULES:\n" +
        "1. Use ONLY the colors, spacing and typography from this data. Do not invent anything and do not add " +
        "visual properties that are not here.\n" +
        "2. visualSpec maps straight onto CSS:\n" +
        "   - fills[].color → background-color\n" +
        "   - strokes[].color and weight → border\n" +
        "   - effects[] → box-shadow (DROP_SHADOW: offset.x offset.y radius spread color)\n" +
        "   - cornerRadius → border-radius\n" +
        "   - layout.mode HORIZONTAL → flex-direction: row, VERTICAL → column\n" +
        "   - layout.padding* → padding, layout.itemSpacing → gap\n" +
        "   - layout.primaryAxisAlign → justify-content, counterAxisAlign → align-items\n" +
        "   - typography → font-family, font-size, font-weight, line-height, letter-spacing\n" +
        "   - Variant specifications are delta-encoded: the base variant carries the full visualSpec, its neighbours carry " +
        "a visualSpecDelta with the differences only (null means the property is absent on that variant). The full variant " +
        "specification = the base visualSpec with visualSpecDelta applied on top. A variant with neither field looks exactly like the base.\n" +
        "3. Do not add decoration — colored borders, accents, dividers, gradients — that is not in visualSpec.\n" +
        "4. imageUrl, when present, is the final visual authority.\n" +
        "5. A style resolvedValue holds the exact colors and typography of the system: take them as they are, do not substitute similar ones.\n" +
        "6. A component properties block is its API. VARIANT selects visual variants, BOOLEAN turns parts on and off, " +
        "TEXT accepts a string.\n" +
        "7. When this lands on an existing library (shadcn, MUI, Chakra), override its theme with the exact values from " +
        "here rather than mixing them with its defaults.";

    /// <summary>Payload size in kilobytes — drives the compression level.</summary>
    public static double SizeKb(JsonNode node) =>
        node.ToJsonString().Length / 1024.0;

    /// <summary>A size in kilobytes rounded to a whole number for messages, independent of the culture.</summary>
    public static string FormatKb(double value) => value.ToString("0", CultureInfo.InvariantCulture);
}
