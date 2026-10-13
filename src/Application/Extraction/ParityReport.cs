using System.Globalization;

namespace Snail.MCP.Figma.Application.Extraction;

/// <summary>Design-versus-code check; only fields present on both sides are compared.</summary>
public static class ParityReport
{
    /// <summary>Fields where a mismatch is visible to the eye and therefore serious.</summary>
    private static readonly HashSet<string> HighSeverity = new(StringComparer.Ordinal)
    {
        "fills", "strokes", "fontSize", "fontName", "cornerRadius", "layoutMode",
    };

    /// <summary>Numeric tolerance: a pixel either way is rounding, not a mismatch.</summary>
    private const double NumericTolerance = 0.5;

    public static JsonObject Compare(JsonObject design, JsonObject code)
    {
        var matches = new JsonArray();
        var mismatches = new JsonArray();
        var missingInCode = new JsonArray();

        foreach (var (key, codeValue) in code)
        {
            var designValue = design[key];

            if (designValue is null)
            {
                missingInCode.Add((JsonNode)new JsonObject
                {
                    ["field"] = key,
                    ["codeValue"] = codeValue?.DeepClone(),
                    ["note"] = "The design has no such property — it may be redundant in the code.",
                });
                continue;
            }

            if (AreEquivalent(designValue, codeValue))
            {
                matches.Add((JsonNode)key);
                continue;
            }

            mismatches.Add((JsonNode)new JsonObject
            {
                ["field"] = key,
                ["designValue"] = designValue.DeepClone(),
                ["codeValue"] = codeValue?.DeepClone(),
                ["severity"] = HighSeverity.Contains(key) ? "high" : "low",
                ["fixInCode"] = $"Bring {key} to the value from the design: {Describe(designValue)}",
                ["fixInFigma"] = $"Or change {key} in Figma to {Describe(codeValue)}",
            });
        }

        var compared = matches.Count + mismatches.Count;
        var score = compared == 0 ? 0 : (double)matches.Count / compared;

        return new JsonObject
        {
            ["parityScore"] = Math.Round(score, 3),
            ["comparedFields"] = compared,
            ["matches"] = matches,
            ["mismatches"] = mismatches,
            ["notInDesign"] = missingInCode,
            ["summary"] = compared == 0
                ? "Nothing to compare: codeSpec has no fields that the design has."
                : $"{matches.Count} of {compared} matched, {mismatches.Count} mismatched",
        };
    }

    private static bool AreEquivalent(JsonNode? design, JsonNode? code)
    {
        if (JsonNode.DeepEquals(design, code))
        {
            return true;
        }

        if (TryGetNumber(design, out var a) && TryGetNumber(code, out var b))
        {
            return Math.Abs(a - b) <= NumericTolerance;
        }

        if (design is JsonArray designPaints && code is JsonValue)
        {
            var designHex = FirstSolidHex(designPaints);
            var codeHex = code.GetValue<string>()?.Trim();

            return designHex is not null && codeHex is not null &&
                   string.Equals(designHex, Normalize(codeHex), StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? FirstSolidHex(JsonArray paints)
    {
        foreach (var paint in paints)
        {
            if (paint?["type"]?.GetValue<string>() != "SOLID")
            {
                continue;
            }

            var color = paint["color"];

            if (color is null)
            {
                continue;
            }

            var r = (int)Math.Round(Math.Clamp(color["r"]?.GetValue<double>() ?? 0, 0, 1) * 255);
            var g = (int)Math.Round(Math.Clamp(color["g"]?.GetValue<double>() ?? 0, 0, 1) * 255);
            var b = (int)Math.Round(Math.Clamp(color["b"]?.GetValue<double>() ?? 0, 0, 1) * 255);

            return $"#{r:X2}{g:X2}{b:X2}";
        }

        return null;
    }

    /// <summary>Expands 3-digit hex to 6 digits, otherwise #FFF never equals #FFFFFF.</summary>
    private static string Normalize(string hex)
    {
        if (hex.Length == 4 && hex[0] == '#')
        {
            return $"#{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}";
        }

        return hex;
    }

    private static bool TryGetNumber(JsonNode? node, out double value)
    {
        value = 0;

        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<double>(out value))
        {
            return true;
        }

        return jsonValue.TryGetValue<string>(out var text) &&
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Describe(JsonNode? node) => node?.ToJsonString() ?? "null";
}
