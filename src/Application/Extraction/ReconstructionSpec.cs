namespace Snail.MCP.Figma.Application.Extraction;

/// <summary>Turns a Figma node into a spec the plugin can rebuild through <c>figma_create_child</c>.</summary>
public static class ReconstructionSpec
{
    /// <summary>Default sizes for nodes whose size could not be read; without them the plugin creates a degenerate node.</summary>
    private static readonly Dictionary<string, (double Width, double Height)> FallbackSize = new(StringComparer.Ordinal)
    {
        ["TEXT"] = (100, 20),
        ["COMPONENT_SET"] = (200, 100),
        ["ELLIPSE"] = (8, 8),
    };

    /// <summary>Nodes whose position and size are set by the parent.</summary>
    private static readonly HashSet<string> SizelessTypes = new(StringComparer.Ordinal) { "GROUP", "SECTION" };

    /// <summary>Builds the node spec, recursing into children.</summary>
    public static JsonObject Extract(JsonNode node)
    {
        var spec = new JsonObject
        {
            ["name"] = node["name"]?.DeepClone(),
        };

        var type = node["type"]?.GetValue<string>() ?? "FRAME";

        spec["type"] = type == "INSTANCE" ? "FRAME" : type;

        ExtractPosition(node, spec, type);
        ExtractSize(node, spec, type);
        ExtractVisuals(node, spec);
        ExtractLayout(node, spec);

        if (type == "TEXT")
        {
            ExtractText(node, spec);
        }

        if (type == "COMPONENT" && node["variantProperties"] is { } variantProperties)
        {
            spec["variantProperties"] = variantProperties.DeepClone();
        }

        if (node["children"] is JsonArray children)
        {
            spec["children"] = new JsonArray(children
                .Where(child => child is not null)
                .Select(child => (JsonNode)Extract(child!))
                .ToArray());
        }

        return spec;
    }

    /// <summary>Checks that the spec can be handed to the plugin at all.</summary>
    public static (bool Valid, IReadOnlyList<string> Errors) Validate(JsonNode? spec)
    {
        var errors = new List<string>();

        if (spec is not JsonObject obj)
        {
            return (false, ["the specification must be a JSON object"]);
        }

        if (string.IsNullOrWhiteSpace(obj["name"]?.GetValue<string>()))
        {
            errors.Add("the required field name is missing");
        }

        if (string.IsNullOrWhiteSpace(obj["type"]?.GetValue<string>()))
        {
            errors.Add("the required field type is missing");
        }

        if (obj["children"] is JsonArray children)
        {
            for (var i = 0; i < children.Count; i++)
            {
                var (childValid, childErrors) = Validate(children[i]);

                if (!childValid)
                {
                    errors.AddRange(childErrors.Select(error => $"children[{i}]: {error}"));
                }
            }
        }

        return (errors.Count == 0, errors);
    }

    /// <summary>Variant names of a set, in <c>Axis=Value, Axis=Value</c> form.</summary>
    public static IReadOnlyList<string> ListVariants(JsonNode componentSet) =>
        (componentSet["children"] as JsonArray)?
        .Select(child => child?["name"]?.GetValue<string>())
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name!)
        .ToArray() ?? [];

    /// <summary>Spec of one set variant by name.</summary>
    public static JsonObject? ExtractVariant(JsonNode componentSet, string variantName)
    {
        var variant = (componentSet["children"] as JsonArray)?
            .FirstOrDefault(child =>
                string.Equals(child?["name"]?.GetValue<string>(), variantName, StringComparison.OrdinalIgnoreCase));

        return variant is null ? null : Extract(variant);
    }

    /// <summary>Fills in Plugin API shape; invisible and unknown types are dropped.</summary>
    public static JsonArray ConvertPaints(JsonNode? paints)
    {
        var result = new JsonArray();

        if (paints is not JsonArray array)
        {
            return result;
        }

        foreach (var paint in array)
        {
            if (paint is null || paint["visible"]?.GetValue<bool>() == false)
            {
                continue;
            }

            var type = paint["type"]?.GetValue<string>();
            var opacity = paint["opacity"]?.GetValue<double>() ?? 1;

            if (type == "SOLID")
            {
                var color = paint["color"];
                result.Add(new JsonObject
                {
                    ["type"] = "SOLID",
                    ["color"] = new JsonObject
                    {
                        ["r"] = color?["r"]?.GetValue<double>() ?? 0,
                        ["g"] = color?["g"]?.GetValue<double>() ?? 0,
                        ["b"] = color?["b"]?.GetValue<double>() ?? 0,
                        ["a"] = opacity * (color?["a"]?.GetValue<double>() ?? 1),
                    },
                    ["opacity"] = opacity,
                });
            }
            else if (type is not null && type.StartsWith("GRADIENT", StringComparison.Ordinal))
            {
                result.Add(new JsonObject
                {
                    ["type"] = type,
                    ["gradientStops"] = paint["gradientStops"]?.DeepClone() ?? new JsonArray(),
                    ["opacity"] = opacity,
                });
            }
            else if (type == "IMAGE")
            {
                result.Add(new JsonObject
                {
                    ["type"] = "IMAGE",
                    ["scaleMode"] = paint["scaleMode"]?.GetValue<string>() ?? "FILL",
                    ["imageRef"] = paint["imageRef"]?.DeepClone(),
                    ["opacity"] = opacity,
                });
            }
        }

        return result;
    }

    private static void ExtractPosition(JsonNode node, JsonObject spec, string type)
    {
        if (SizelessTypes.Contains(type))
        {
            return;
        }

        spec["x"] = node["x"]?.GetValue<double>() ?? node["absoluteBoundingBox"]?["x"]?.GetValue<double>() ?? 0;
        spec["y"] = node["y"]?.GetValue<double>() ?? node["absoluteBoundingBox"]?["y"]?.GetValue<double>() ?? 0;

        foreach (var key in (string[])["layoutSizingHorizontal", "layoutSizingVertical"])
        {
            if (node[key] is { } value)
            {
                spec[key] = value.DeepClone();
            }
        }
    }

    private static void ExtractSize(JsonNode node, JsonObject spec, string type)
    {
        if (SizelessTypes.Contains(type))
        {
            return;
        }

        var hugs = node["layoutSizingHorizontal"]?.GetValue<string>() == "HUG" ||
                   node["layoutSizingVertical"]?.GetValue<string>() == "HUG";
        var isContainer = type is "COMPONENT" or "FRAME" or "INSTANCE";

        if (hugs && !isContainer)
        {
            return;
        }

        var fallback = FallbackSize.TryGetValue(type, out var size) ? size : (50, 50);

        spec["width"] = node["width"]?.GetValue<double>()
                        ?? node["absoluteBoundingBox"]?["width"]?.GetValue<double>()
                        ?? fallback.Item1;

        spec["height"] = node["height"]?.GetValue<double>()
                         ?? node["absoluteBoundingBox"]?["height"]?.GetValue<double>()
                         ?? fallback.Item2;
    }

    private static void ExtractVisuals(JsonNode node, JsonObject spec)
    {
        if (node["opacity"]?.GetValue<double>() is { } opacity && Math.Abs(opacity - 1) > double.Epsilon)
        {
            spec["opacity"] = opacity;
        }

        var fills = ConvertPaints(node["fills"]);

        if (fills.Count > 0)
        {
            spec["fills"] = fills;
        }

        var strokes = ConvertPaints(node["strokes"]);

        if (strokes.Count > 0)
        {
            spec["strokes"] = strokes;

            if (node["strokeWeight"] is { } weight)
            {
                spec["strokeWeight"] = weight.DeepClone();
            }
        }

        if (node["effects"] is JsonArray effects && effects.Count > 0)
        {
            spec["effects"] = effects.DeepClone();
        }

        foreach (var key in (string[])["cornerRadius", "rectangleCornerRadii"])
        {
            if (node[key] is { } value)
            {
                spec[key] = value.DeepClone();
            }
        }
    }

    private static void ExtractLayout(JsonNode node, JsonObject spec)
    {
        var layoutMode = node["layoutMode"]?.GetValue<string>();

        if (layoutMode is null or "NONE")
        {
            return;
        }

        spec["layoutMode"] = layoutMode;

        foreach (var key in (string[])
                 [
                     "primaryAxisSizingMode", "counterAxisSizingMode", "itemSpacing",
                     "paddingLeft", "paddingRight", "paddingTop", "paddingBottom",
                     "primaryAxisAlignItems", "counterAxisAlignItems",
                 ])
        {
            if (node[key] is { } value)
            {
                spec[key] = value.DeepClone();
            }
        }
    }

    /// <summary>Text properties live on the node in the Plugin API and under <c>style</c> in REST; both are read.</summary>
    private static void ExtractText(JsonNode node, JsonObject spec)
    {
        if (node["characters"] is { } characters)
        {
            spec["characters"] = characters.DeepClone();
        }

        var style = node["style"];

        spec["fontSize"] = node["fontSize"]?.DeepClone() ?? style?["fontSize"]?.DeepClone();

        if (node["fontName"] is { } fontName)
        {
            spec["fontName"] = fontName.DeepClone();
        }
        else if (style?["fontFamily"] is { } family)
        {
            spec["fontName"] = new JsonObject
            {
                ["family"] = family.DeepClone(),
                ["style"] = style["fontWeight"]?.DeepClone() ?? JsonValue.Create("Regular"),
            };
        }

        foreach (var key in (string[])["textAlignHorizontal", "textAlignVertical", "letterSpacing", "lineHeight"])
        {
            var value = node[key]?.DeepClone() ?? style?[key]?.DeepClone();

            if (value is not null)
            {
                spec[key] = value;
            }
        }
    }
}
