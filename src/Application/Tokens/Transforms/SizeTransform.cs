using System.Globalization;

namespace Snail.MCP.Figma.Application.Tokens.Transforms;

/// <summary>Size conversion between px, rem, pt and dp.</summary>
public static class SizeTransform
{
    /// <summary>Base font size for px ↔ rem, the browser default.</summary>
    public const double DefaultRemBase = 16;

    /// <summary>Number and unit from a string like <c>16px</c>; a bare number is pixels.</summary>
    public static (double Value, string Unit)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var index = 0;

        while (index < trimmed.Length && (char.IsDigit(trimmed[index]) || trimmed[index] is '.' or '-' or '+'))
        {
            index++;
        }

        if (index == 0)
        {
            return null;
        }

        if (!double.TryParse(trimmed[..index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var unit = trimmed[index..].Trim();

        return (value, string.IsNullOrEmpty(unit) ? "px" : unit.ToLowerInvariant());
    }

    /// <summary>Converts a size to the target unit; percentages and other units pass through.</summary>
    public static string Convert(string text, string targetUnit, double remBase = DefaultRemBase)
    {
        if (Parse(text) is not { } parsed)
        {
            return text;
        }

        var (value, unit) = parsed;
        var target = targetUnit.ToLowerInvariant();

        if (unit == target)
        {
            return Render(value, unit);
        }

        var pixels = unit switch
        {
            "px" or "dp" => value,
            "rem" or "em" => value * remBase,
            "pt" => value * 96d / 72d,
            _ => double.NaN,
        };

        if (double.IsNaN(pixels))
        {
            return text;
        }

        return target switch
        {
            "px" => Render(pixels, "px"),
            "dp" => Render(pixels, "dp"),
            "rem" => Render(pixels / remBase, "rem"),
            "pt" => Render(pixels * 72d / 96d, "pt"),
            _ => text,
        };
    }

    /// <summary>Number to string with unit; zero is written without a unit, as CSS convention.</summary>
    public static string Render(double value, string unit)
    {
        if (Math.Abs(value) < 1e-9)
        {
            return "0";
        }

        return Math.Round(value, 5).ToString(CultureInfo.InvariantCulture) + unit;
    }
}
