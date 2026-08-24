using System.Globalization;

namespace Snail.MCP.Figma.Application.Extraction;

/// <summary>Collects token-like values straight from the design tree where Figma variables are unavailable.</summary>
public sealed class StyleExtractor
{
    private readonly Dictionary<string, ExtractedStyle> _styles = new(StringComparer.Ordinal);
    private int _colorIndex;
    private int _typographyIndex;

    /// <summary>Walks the tree and returns the found values without duplicates.</summary>
    public IReadOnlyList<ExtractedStyle> Extract(JsonNode root, int maxDepth = 12)
    {
        _styles.Clear();
        _colorIndex = 0;
        _typographyIndex = 0;

        Walk(root["document"] ?? root, 0, maxDepth);

        return _styles.Values.ToArray();
    }

    private void Walk(JsonNode? node, int depth, int maxDepth)
    {
        if (node is not JsonObject obj || depth > maxDepth)
        {
            return;
        }

        var name = obj["name"]?.GetValue<string>();
        var id = obj["id"]?.GetValue<string>();

        foreach (var paint in (obj["fills"] as JsonArray) ?? [])
        {
            TryAddColor(paint, name, id, "fill");
        }

        foreach (var paint in (obj["strokes"] as JsonArray) ?? [])
        {
            TryAddColor(paint, name, id, "stroke");
        }

        if (obj["style"] is JsonObject typography)
        {
            TryAddTypography(typography, name, id);
        }

        foreach (var key in (string[])["itemSpacing", "paddingLeft", "paddingRight", "paddingTop", "paddingBottom"])
        {
            if (obj[key]?.GetValue<double>() is { } value && value > 0)
            {
                TryAddSpacing(key, value, name, id);
            }
        }

        if (obj["cornerRadius"]?.GetValue<double>() is { } radius)
        {
            TryAddRadius(radius, name, id);
        }

        foreach (var child in (obj["children"] as JsonArray) ?? [])
        {
            Walk(child, depth + 1, maxDepth);
        }
    }

    private void TryAddColor(JsonNode? paint, string? nodeName, string? nodeId, string usage)
    {
        if (paint is null || paint["type"]?.GetValue<string>() != "SOLID")
        {
            return;
        }

        var color = paint["color"];

        if (color is null)
        {
            return;
        }

        var r = ToByte(color["r"]);
        var g = ToByte(color["g"]);
        var b = ToByte(color["b"]);
        var alpha = (paint["opacity"]?.GetValue<double>() ?? 1) * (color["a"]?.GetValue<double>() ?? 1);

        var hex = $"#{r:X2}{g:X2}{b:X2}";
        var value = alpha < 1
            ? $"rgba({r}, {g}, {b}, {alpha.ToString("0.##", CultureInfo.InvariantCulture)})"
            : hex;

        var key = $"color_{hex}_{alpha:0.##}";
        var category = InferColorCategory(nodeName);

        if (_styles.TryGetValue(key, out var existing))
        {
            if (existing.Category == "color" && category != "color")
            {
                _styles[key] = existing with
                {
                    Name = existing.Name.Replace("color/", $"{category}/", StringComparison.Ordinal),
                    Category = category,
                    SourceNodeName = nodeName,
                    SourceNodeId = nodeId,
                };
            }

            return;
        }

        var tier = _colorIndex < 5 ? "primary" : _colorIndex < 10 ? "secondary" : "tertiary";
        _colorIndex++;

        _styles[key] = new ExtractedStyle(
            key,
            $"{category}/{tier}-{usage}",
            value,
            "COLOR",
            category,
            nodeName,
            nodeId);
    }

    private void TryAddTypography(JsonObject style, string? nodeName, string? nodeId)
    {
        var family = style["fontFamily"]?.GetValue<string>();
        var size = style["fontSize"]?.GetValue<double>();

        if (family is null && size is null)
        {
            return;
        }

        var weight = style["fontWeight"]?.ToJsonString();
        var key = $"typography_{family}_{size}_{weight}";

        if (_styles.ContainsKey(key))
        {
            return;
        }

        var parts = new List<string> { $"font-family: \"{family ?? "Inter"}\"" };

        if (size is { } fontSize)
        {
            parts.Add($"font-size: {fontSize.ToString("0.##", CultureInfo.InvariantCulture)}px");
        }

        if (weight is not null)
        {
            parts.Add($"font-weight: {weight}");
        }

        if (style["lineHeightPx"]?.GetValue<double>() is { } lineHeight)
        {
            parts.Add($"line-height: {lineHeight.ToString("0.##", CultureInfo.InvariantCulture)}px");
        }

        if (style["letterSpacing"]?.GetValue<double>() is { } letterSpacing && Math.Abs(letterSpacing) > double.Epsilon)
        {
            parts.Add($"letter-spacing: {letterSpacing.ToString("0.##", CultureInfo.InvariantCulture)}px");
        }

        _styles[key] = new ExtractedStyle(
            key,
            GenerateTypographyName(nodeName, _typographyIndex++),
            string.Join("; ", parts),
            "TYPOGRAPHY",
            "text",
            nodeName,
            nodeId);
    }

    private void TryAddSpacing(string kind, double value, string? nodeName, string? nodeId)
    {
        var key = $"spacing_{kind}_{value}";

        if (_styles.ContainsKey(key))
        {
            return;
        }

        var rounded = Math.Round(value / 4) * 4;
        _styles[key] = new ExtractedStyle(
            key,
            $"{kind}/{(rounded > 0 ? rounded : value).ToString("0.##", CultureInfo.InvariantCulture)}",
            $"{value.ToString("0.##", CultureInfo.InvariantCulture)}px",
            "SPACING",
            kind,
            nodeName,
            nodeId);
    }

    private void TryAddRadius(double value, string? nodeName, string? nodeId)
    {
        var key = $"radius_{value}";

        if (_styles.ContainsKey(key))
        {
            return;
        }

        _styles[key] = new ExtractedStyle(
            key,
            $"radius/{CategorizeRadius(value)}",
            $"{value.ToString("0.##", CultureInfo.InvariantCulture)}px",
            "RADIUS",
            "border",
            nodeName,
            nodeId);
    }

    /// <summary>Tool-response summary: a flat name → value map plus counters.</summary>
    public static JsonObject Format(IReadOnlyList<ExtractedStyle> styles)
    {
        var tokens = new JsonObject();

        foreach (var style in styles)
        {
            tokens[style.Name] = style.Value;
        }

        var counts = new JsonObject();

        foreach (var group in styles.GroupBy(s => s.Type))
        {
            counts[group.Key] = group.Count();
        }

        counts["total"] = styles.Count;

        return new JsonObject
        {
            ["tokens"] = tokens,
            ["counts"] = counts,
            ["note"] = "These are values pulled out of the design, not real Figma variables " +
                       "(the Variables API is Enterprise-only).",
        };
    }

    internal static string InferColorCategory(string? nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return "color";
        }

        var name = nodeName.ToLowerInvariant();

        if (name.Contains("background") || name.Contains("bg")) return "background";
        if (name.Contains("text") || name.Contains("label") || name.Contains("title")) return "text";
        if (name.Contains("border") || name.Contains("stroke")) return "border";
        if (name.Contains("primary") || name.Contains("secondary") || name.Contains("accent")) return "theme";
        if (name.Contains("success") || name.Contains("error") || name.Contains("warning")) return "semantic";

        return "color";
    }

    internal static string CategorizeRadius(double value) => value switch
    {
        0 => "none",
        <= 2 => "xs",
        <= 4 => "sm",
        <= 8 => "md",
        <= 16 => "lg",
        <= 24 => "xl",
        _ => "xxl",
    };

    private static string GenerateTypographyName(string? nodeName, int index)
    {
        if (!string.IsNullOrWhiteSpace(nodeName))
        {
            var name = nodeName.ToLowerInvariant();
            var slug = System.Text.RegularExpressions.Regex.Replace(name, "[^a-z0-9]+", "-").Trim('-');

            if (name.Contains("heading") || name.Contains("h1") || name.Contains("h2")) return $"heading/{slug}";
            if (name.Contains("body") || name.Contains("paragraph")) return $"body/{slug}";
            if (name.Contains("caption") || name.Contains("label")) return $"caption/{slug}";
        }

        return $"text/style-{index}";
    }

    private static int ToByte(JsonNode? channel) =>
        (int)Math.Round(Math.Clamp(channel?.GetValue<double>() ?? 0, 0, 1) * 255);
}

/// <summary>One extracted style value.</summary>
/// <param name="Id">Deduplication key: equal values collapse.</param>
/// <param name="Name">Suggested token name.</param>
/// <param name="Value">Value in CSS-ready form.</param>
/// <param name="Type">COLOR, TYPOGRAPHY, SPACING or RADIUS.</param>
/// <param name="Category">Semantic group inferred from the node name.</param>
public sealed record ExtractedStyle(
    string Id,
    string Name,
    string Value,
    string Type,
    string Category,
    string? SourceNodeName,
    string? SourceNodeId);
