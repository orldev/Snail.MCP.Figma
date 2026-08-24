using Snail.MCP.Figma.Application.Extraction;

namespace Snail.MCP.Figma.Tests.Application.Extraction;

public class VisualSpecTests
{
    private static JsonNode Node(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void FillsStrokesEffectsAndLayoutBecomeCssReadyValues()
    {
        var spec = DesignSystemKit.ExtractVisualSpec(Node("""
            {
              "type": "FRAME",
              "fills": [
                {"type": "SOLID", "color": {"r": 0.09, "g": 0.09, "b": 0.09}},
                {"type": "SOLID", "visible": false, "color": {"r": 1, "g": 0, "b": 0}}
              ],
              "strokes": [{"type": "SOLID", "color": {"r": 0.59, "g": 0.28, "b": 1}}],
              "strokeWeight": 1,
              "strokeAlign": "INSIDE",
              "effects": [{"type": "DROP_SHADOW", "color": {"r": 0, "g": 0, "b": 0}, "radius": 4, "offset": {"x": 0, "y": 2}}],
              "cornerRadius": 8,
              "opacity": 0.9,
              "layoutMode": "HORIZONTAL",
              "paddingLeft": 16,
              "itemSpacing": 8,
              "primaryAxisAlignItems": "CENTER"
            }
            """));

        Assert.NotNull(spec);

        Assert.Single(spec!["fills"]!.AsArray());
        Assert.Equal("#171717", spec["fills"]![0]!["color"]!.GetValue<string>());

        Assert.Equal(1, spec["strokes"]![0]!["weight"]!.GetValue<double>());
        Assert.Equal("INSIDE", spec["strokes"]![0]!["align"]!.GetValue<string>());

        Assert.Equal(8, spec["cornerRadius"]!.GetValue<double>());
        Assert.Equal("HORIZONTAL", spec["layout"]!["mode"]!.GetValue<string>());
        Assert.Equal(8, spec["layout"]!["itemSpacing"]!.GetValue<double>());
        Assert.Equal("CENTER", spec["layout"]!["primaryAxisAlign"]!.GetValue<string>());
    }

    [Fact]
    public void NodeWithNothingWorthReproducingYieldsNoSpec()
    {
        Assert.Null(DesignSystemKit.ExtractVisualSpec(Node("""{"type": "FRAME", "opacity": 1}""")));
    }

    [Fact]
    public void TypographyIsReadOnlyFromTextNodes()
    {
        var spec = DesignSystemKit.ExtractVisualSpec(Node("""
            {"type": "TEXT", "style": {"fontFamily": "Inter", "fontSize": 14, "lineHeightPx": 20}}
            """));

        Assert.Equal("Inter", spec!["typography"]!["fontFamily"]!.GetValue<string>());
        Assert.Equal(20, spec["typography"]!["lineHeight"]!.GetValue<double>());
    }
}

public class VariantDeltaTests
{
    private static JsonArray Variants() => (JsonArray)JsonNode.Parse("""
        [
          {"name": "Size=S", "id": "1:1", "visualSpec": {"cornerRadius": 8, "opacity": 0.5, "fills": [{"color": "#000000"}]}},
          {"name": "Size=M", "id": "1:2", "visualSpec": {"cornerRadius": 8, "opacity": 0.5, "fills": [{"color": "#000000"}]}},
          {"name": "Size=L", "id": "1:3", "visualSpec": {"cornerRadius": 12, "fills": [{"color": "#000000"}]}}
        ]
        """)!;

    [Fact]
    public void BaseKeepsTheFullSpecAndSiblingsCarryOnlyDifferences()
    {
        var variants = Variants();
        DesignSystemKit.DeltaEncodeVariants(variants);

        Assert.NotNull(variants[0]!["visualSpec"]);

        Assert.Null(variants[1]!["visualSpec"]);
        Assert.Null(variants[1]!["visualSpecDelta"]);

        var delta = variants[2]!["visualSpecDelta"]!.AsObject();
        Assert.Equal(12, delta["cornerRadius"]!.GetValue<double>());
        Assert.True(delta.ContainsKey("opacity"));
        Assert.Null(delta["opacity"]);
        Assert.False(delta.ContainsKey("fills"));
    }

    [Fact]
    public void NothingHappensWhenNoVariantHasASpec()
    {
        var variants = (JsonArray)JsonNode.Parse("""[{"name": "Size=S", "id": "1:1"}]""")!;

        DesignSystemKit.DeltaEncodeVariants(variants);

        Assert.Null(variants[0]!["visualSpecDelta"]);
    }
}

public class KitCompressionTests
{
    private static JsonObject Kit() => (JsonObject)JsonNode.Parse("""
        {
          "tokens": {
            "collections": [
              {"id": "c1", "name": "Core", "variables": [
                {"id": "v1", "name": "color/bg", "type": "COLOR", "valuesByMode": {"m1": "#fff"}}
              ]}
            ],
            "summary": {"totalCollections": 1, "totalVariables": 1}
          },
          "components": {
            "items": [
              {"id": "1:1", "name": "Button", "imageUrl": "https://example.test/a.png",
               "visualSpec": {"cornerRadius": 8},
               "properties": {"Size": {"type": "VARIANT", "defaultValue": "M", "variantOptions": ["S","M"]}},
               "variants": [{"name": "Size=S", "id": "1:2", "visualSpec": {"cornerRadius": 8}}]}
            ],
            "summary": {"totalComponents": 1, "totalComponentSets": 1}
          },
          "styles": {
            "items": [{"key": "k1", "name": "Body", "styleType": "TEXT", "resolvedValue": {"typography": {"fontSize": 14}}}],
            "summary": {"totalStyles": 1}
          }
        }
        """)!;

    [Fact]
    public void SummaryDropsVariantSpecsButKeepsTheSetSpec()
    {
        var compressed = DesignSystemKit.Compress(Kit(), DesignSystemKit.Summary);
        var item = compressed["components"]!["items"]![0]!;

        Assert.NotNull(item["visualSpec"]);
        Assert.Null(item["variants"]![0]!["visualSpec"]);

        Assert.Null(item["imageUrl"]);
    }

    [Fact]
    public void InventoryKeepsNamesAndTypesAndDropsValues()
    {
        var compressed = DesignSystemKit.Compress(Kit(), DesignSystemKit.Inventory);

        Assert.Null(compressed["tokens"]!["collections"]![0]!["variables"]![0]!["valuesByMode"]);
        Assert.Null(compressed["styles"]!["items"]![0]!["resolvedValue"]);

        var properties = compressed["components"]!["items"]![0]!["properties"]!["Size"]!.AsObject();
        Assert.Equal("VARIANT", properties["type"]!.GetValue<string>());
        Assert.False(properties.ContainsKey("variantOptions"));
    }

    [Fact]
    public void CompactThrowsAwayEverythingExceptTheShapeOfTheSystem()
    {
        var compressed = DesignSystemKit.Compress(Kit(), DesignSystemKit.Compact);

        Assert.Empty(compressed["tokens"]!["collections"]!.AsArray());
        Assert.Equal(1, compressed["tokens"]!["summary"]!["totalVariables"]!.GetValue<int>());
        Assert.Null(compressed["styles"]!["items"]![0]!["resolvedValue"]);
        Assert.Null(compressed["components"]!["items"]![0]!["visualSpec"]);
    }

    [Theory]
    [InlineData("full", 10, null)]
    [InlineData("full", 150, DesignSystemKit.Summary)]
    [InlineData("full", 300, DesignSystemKit.Inventory)]
    [InlineData("full", 900, DesignSystemKit.Compact)]
    [InlineData("summary", 10, DesignSystemKit.Summary)]
    [InlineData("compact", 10, DesignSystemKit.Compact)]
    public void SizeUpgradesTheCompressionLevelRegardlessOfWhatWasAsked(string format, double sizeKb, string? expected)
    {
        Assert.Equal(expected, DesignSystemKit.CompressionLevel(format, sizeKb));
    }

    [Fact]
    public void CompactRequestIsNeverDowngradedByASmallPayload()
    {
        Assert.Equal(DesignSystemKit.Compact, DesignSystemKit.CompressionLevel("compact", 250));
    }
}
