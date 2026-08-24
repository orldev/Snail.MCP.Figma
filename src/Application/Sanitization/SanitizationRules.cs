using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Application.Sanitization;

/// <summary>What the sanitizer strips on each profile.</summary>
public static class SanitizationRules
{
    /// <summary>Vector geometry: most of the payload and useless to an LLM.</summary>
    private static readonly string[] GeometryKeys =
    [
        "vectorNetwork",
        "vectorPaths",
        "fillGeometry",
        "strokeGeometry",
        "absoluteRenderBounds",
    ];

    /// <summary>Absolute coordinates, stripped only on aggressive; mutations need the plugin's relative <c>x</c>/<c>y</c> anyway.</summary>
    private static readonly string[] AbsoluteCoordinateKeys =
    [
        "absoluteBoundingBox",
    ];

    /// <summary>Housekeeping fields Figma always returns that affect neither understanding nor editing.</summary>
    private static readonly string[] NoiseKeys =
    [
        "componentPropertyReferences",
        "interactions",
        "devStatus",
    ];

    /// <summary>Default values: a field equal to its default carries no information; the list is deliberately conservative.</summary>
    private static readonly Dictionary<string, JsonNode?> Defaults = new(StringComparer.Ordinal)
    {
        ["visible"] = JsonValue.Create(true),
        ["locked"] = JsonValue.Create(false),
        ["isMask"] = JsonValue.Create(false),
        ["clipsContent"] = JsonValue.Create(true),
        ["opacity"] = JsonValue.Create(1.0),
        ["blendMode"] = JsonValue.Create("PASS_THROUGH"),
        ["preserveRatio"] = JsonValue.Create(false),
        ["constrainProportions"] = JsonValue.Create(false),
        ["layoutAlign"] = JsonValue.Create("INHERIT"),
        ["layoutGrow"] = JsonValue.Create(0.0),
        ["layoutPositioning"] = JsonValue.Create("AUTO"),
        ["scrollBehavior"] = JsonValue.Create("SCROLLS"),
        ["strokeAlign"] = JsonValue.Create("INSIDE"),
        ["cornerRadius"] = JsonValue.Create(0.0),
        ["cornerSmoothing"] = JsonValue.Create(0.0),
    };

    /// <summary>Fields that must survive every profile: without them a node is not addressable.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    {
        "id", "name", "type", "children", "key", "componentId", "componentSetId",
    };

    /// <summary>Keys removed entirely on the given profile.</summary>
    public static IReadOnlySet<string> DroppedKeys(SanitizationProfile profile) => profile switch
    {
        SanitizationProfile.Aggressive => AggressiveDropped,
        SanitizationProfile.Balanced => BalancedDropped,
        _ => EmptySet,
    };

    private static readonly HashSet<string> AggressiveDropped =
        new(GeometryKeys.Concat(AbsoluteCoordinateKeys).Concat(NoiseKeys), StringComparer.Ordinal);

    private static readonly HashSet<string> BalancedDropped = new(GeometryKeys, StringComparer.Ordinal);

    private static readonly HashSet<string> EmptySet = new(StringComparer.Ordinal);

    /// <summary>The value equals the Figma default and can be omitted.</summary>
    public static bool IsDefaultValue(string key, JsonNode? value)
    {
        if (Protected.Contains(key) || value is null)
        {
            return false;
        }

        return Defaults.TryGetValue(key, out var @default) && JsonNode.DeepEquals(value, @default);
    }

    /// <summary>The field must survive any sanitization.</summary>
    public static bool IsProtected(string key) => Protected.Contains(key);

    /// <summary>All-zero text style override array: as long as the text, with zero information.</summary>
    public static bool IsAllZeroOverrides(string key, JsonNode? value)
    {
        if (key != "characterStyleOverrides" || value is not JsonArray array)
        {
            return false;
        }

        return array.All(item => item is JsonValue && item.ToJsonString() is "0" or "0.0");
    }
}
