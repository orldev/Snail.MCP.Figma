using System.Globalization;
using Snail.MCP.Figma.Application.Tokens.Transforms;

namespace Snail.MCP.Figma.Application.Tokens.Formatters;

/// <summary>Shared value rendering for all formatters: color and size transforms, composite expansion.</summary>
public static class ValueRenderer
{
    /// <summary>Literal into text with the target format's transforms applied.</summary>
    public static string Render(JsonNode? literal, string type, FormatOptions options)
    {
        if (literal is null)
        {
            return string.Empty;
        }

        if (literal is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return RenderScalarText(text, type, options);
            }

            if (value.TryGetValue<bool>(out var flag))
            {
                return flag ? "true" : "false";
            }

            if (value.TryGetValue<double>(out var number))
            {
                return type == TokenTypes.Dimension
                    ? SizeTransform.Convert(
                        $"{number.ToString(CultureInfo.InvariantCulture)}px",
                        options.SizeUnit ?? "px",
                        options.RemBase)
                    : number.ToString(CultureInfo.InvariantCulture);
            }
        }

        return literal.ToJsonString();
    }

    private static string RenderScalarText(string text, string type, FormatOptions options)
    {
        if (type == TokenTypes.Color)
        {
            return ColorTransform.Convert(text, options.ColorFormat);
        }

        if (type == TokenTypes.Dimension && options.SizeUnit is { } unit)
        {
            return SizeTransform.Convert(text, unit, options.RemBase);
        }

        return text;
    }

    /// <summary>Shadow into CSS; shadow arrays are joined with commas.</summary>
    public static string? RenderShadow(JsonNode? shadow, FormatOptions options)
    {
        if (shadow is JsonArray array)
        {
            var parts = array
                .Select(item => RenderShadow(item, options))
                .Where(part => part is not null)
                .ToArray();

            return parts.Length == 0 ? null : string.Join(", ", parts);
        }

        if (shadow is not JsonObject fields)
        {
            return null;
        }

        var color = fields["color"]?.GetValue<string>();
        var x = WithUnit(fields["offsetX"], options);
        var y = WithUnit(fields["offsetY"], options);
        var blur = WithUnit(fields["blur"], options);

        if (color is null || x is null || y is null || blur is null)
        {
            return null;
        }

        var spread = WithUnit(fields["spread"], options);
        var inset = fields["inset"]?.GetValue<bool>() == true ? "inset " : string.Empty;

        return $"{inset}{x} {y} {blur}{(spread is null ? string.Empty : $" {spread}")} " +
               $"{ColorTransform.Convert(color, options.ColorFormat)}";
    }

    /// <summary>Typography sub-fields in the order CSS properties expect them.</summary>
    public static IEnumerable<(string Field, string Value)> ExpandTypography(JsonNode? typography, FormatOptions options)
    {
        if (typography is not JsonObject fields)
        {
            yield break;
        }

        foreach (var field in (string[])["fontFamily", "fontSize", "fontWeight", "lineHeight", "letterSpacing"])
        {
            if (fields[field] is not { } value)
            {
                continue;
            }

            var type = field is "fontSize" or "lineHeight" or "letterSpacing" ? TokenTypes.Dimension : TokenTypes.String;

            yield return (field, Render(value, type, options));
        }
    }

    private static string? WithUnit(JsonNode? node, FormatOptions options)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<double>(out var number))
        {
            return SizeTransform.Convert(
                $"{number.ToString(CultureInfo.InvariantCulture)}px",
                options.SizeUnit ?? "px",
                options.RemBase);
        }

        return node.GetValue<string>();
    }
}
