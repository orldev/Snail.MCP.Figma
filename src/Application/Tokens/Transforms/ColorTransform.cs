using System.Globalization;

namespace Snail.MCP.Figma.Application.Tokens.Transforms;

/// <summary>Color parsing and output as hex, rgba, hsl or oklch.</summary>
public static class ColorTransform
{
    /// <summary>Color components in 0..1, as Figma returns them.</summary>
    public readonly record struct Rgba(double R, double G, double B, double A)
    {
        /// <summary>Red channel as a byte in 0..255, clamped and rounded for hex and <c>rgb()</c> output.</summary>
        public int R255 => Quantize(R);

        /// <summary>Green channel as a byte in 0..255, clamped and rounded for hex and <c>rgb()</c> output.</summary>
        public int G255 => Quantize(G);

        /// <summary>Blue channel as a byte in 0..255, clamped and rounded for hex and <c>rgb()</c> output.</summary>
        public int B255 => Quantize(B);

        private static int Quantize(double channel) => (int)Math.Round(Math.Clamp(channel, 0, 1) * 255);
    }

    /// <summary>Parses <c>#RGB</c>, <c>#RRGGBB</c>, <c>#RRGGBBAA</c>, <c>rgb()</c> and <c>rgba()</c>.</summary>
    public static Rgba? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        if (text.StartsWith('#'))
        {
            return ParseHex(text[1..]);
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRgbFunction(text);
        }

        return null;
    }

    /// <summary>Output format from the export config: hex, hex8, rgba, hsl, oklch.</summary>
    public static string Format(Rgba color, string format) => format.ToLowerInvariant() switch
    {
        "hex8" => $"#{color.R255:X2}{color.G255:X2}{color.B255:X2}{(int)Math.Round(Math.Clamp(color.A, 0, 1) * 255):X2}",
        "rgba" => FormatRgba(color),
        "hsl" => FormatHsl(color),
        "oklch" => FormatOklch(color),
        _ => color.A < 1 ? FormatRgba(color) : $"#{color.R255:X2}{color.G255:X2}{color.B255:X2}",
    };

    /// <summary>Converts a color string to another format; unparsable input is returned as-is.</summary>
    public static string Convert(string value, string format) =>
        Parse(value) is { } color ? Format(color, format) : value;

    private static Rgba? ParseHex(string hex)
    {
        if (hex.Length is 3 or 4)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length is not (6 or 8) || !hex.All(Uri.IsHexDigit))
        {
            return null;
        }

        var r = int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var a = hex.Length == 8
            ? int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d
            : 1d;

        return new Rgba(r, g, b, a);
    }

    private static Rgba? ParseRgbFunction(string text)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');

        if (open < 0 || close <= open)
        {
            return null;
        }

        var parts = text[(open + 1)..close]
            .Replace('/', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length is < 3 or > 4)
        {
            return null;
        }

        var channels = new double[4];
        channels[3] = 1;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isPercent = part.EndsWith('%');

            if (!double.TryParse(part.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            channels[i] = i == 3
                ? (isPercent ? number / 100 : number)
                : (isPercent ? number / 100 : number / 255);
        }

        return new Rgba(channels[0], channels[1], channels[2], channels[3]);
    }

    private static string FormatRgba(Rgba color) =>
        color.A < 1
            ? $"rgba({color.R255}, {color.G255}, {color.B255}, {Round(color.A)})"
            : $"rgb({color.R255}, {color.G255}, {color.B255})";

    private static string FormatHsl(Rgba color)
    {
        double r = color.R, g = color.G, b = color.B;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        double hue = 0, saturation = 0;

        if (Math.Abs(max - min) > double.Epsilon)
        {
            var delta = max - min;
            saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);

            if (Math.Abs(max - r) < double.Epsilon)
            {
                hue = (g - b) / delta + (g < b ? 6 : 0);
            }
            else if (Math.Abs(max - g) < double.Epsilon)
            {
                hue = (b - r) / delta + 2;
            }
            else
            {
                hue = (r - g) / delta + 4;
            }

            hue *= 60;
        }

        var body = $"{Round(hue, 0)} {Round(saturation * 100, 1)}% {Round(lightness * 100, 1)}%";

        return color.A < 1 ? $"hsl({body} / {Round(color.A)})" : $"hsl({body})";
    }

    /// <summary>sRGB → OKLCH per CSS Color 4, for palettes defined in perceptual space.</summary>
    private static string FormatOklch(Rgba color)
    {
        var r = ToLinear(color.R);
        var g = ToLinear(color.G);
        var b = ToLinear(color.B);

        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        var okL = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        var okA = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        var okB = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;

        var chroma = Math.Sqrt(okA * okA + okB * okB);
        var hue = Math.Atan2(okB, okA) * 180 / Math.PI;

        if (hue < 0)
        {
            hue += 360;
        }

        var body = $"{Round(okL * 100, 1)}% {Round(chroma, 4)} {Round(hue, 1)}";

        return color.A < 1 ? $"oklch({body} / {Round(color.A)})" : $"oklch({body})";
    }

    private static double ToLinear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static string Round(double value, int digits = 3) =>
        Math.Round(value, digits).ToString(CultureInfo.InvariantCulture);
}
