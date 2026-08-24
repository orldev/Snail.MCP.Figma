using Snail.MCP.Figma.Application.Extraction;

namespace Snail.MCP.Figma.Tests.Application.Extraction;

public class ReconstructionSpecTests
{
    private static JsonNode Button() => JsonNode.Parse("""
        {
          "id": "1:10",
          "name": "Button/Primary",
          "type": "INSTANCE",
          "x": 12, "y": 24, "width": 120, "height": 40,
          "opacity": 1,
          "cornerRadius": 8,
          "layoutMode": "HORIZONTAL",
          "primaryAxisSizingMode": "AUTO",
          "counterAxisSizingMode": "FIXED",
          "itemSpacing": 8,
          "paddingLeft": 16, "paddingRight": 16, "paddingTop": 8, "paddingBottom": 8,
          "fills": [
            {"type": "SOLID", "color": {"r": 0.2, "g": 0.4, "b": 1.0, "a": 1}, "opacity": 1},
            {"type": "SOLID", "visible": false, "color": {"r": 1, "g": 0, "b": 0}}
          ],
          "strokes": [{"type": "SOLID", "color": {"r": 0, "g": 0, "b": 0}}],
          "strokeWeight": 2,
          "children": [
            {
              "id": "1:11", "name": "Label", "type": "TEXT",
              "characters": "Buy",
              "layoutSizingHorizontal": "HUG",
              "style": {"fontFamily": "Inter", "fontSize": 14, "fontWeight": 600, "lineHeightPx": 20}
            }
          ]
        }
        """)!;

    [Fact]
    public void InstanceBecomesFrameBecauseThePluginCannotCreateInstances()
    {
        var spec = ReconstructionSpec.Extract(Button());

        Assert.Equal("FRAME", spec["type"]!.GetValue<string>());
        Assert.Equal("Button/Primary", spec["name"]!.GetValue<string>());
    }

    [Fact]
    public void InvisiblePaintsAreDroppedAndOpacityFoldsIntoAlpha()
    {
        var spec = ReconstructionSpec.Extract(Button());

        var fills = (spec["fills"] as JsonArray)!;
        Assert.Single(fills);
        Assert.Equal(1.0, fills[0]!["color"]!["a"]!.GetValue<double>());
    }

    [Fact]
    public void AutoLayoutPropertiesSurviveIntoTheSpec()
    {
        var spec = ReconstructionSpec.Extract(Button());

        Assert.Equal("HORIZONTAL", spec["layoutMode"]!.GetValue<string>());
        Assert.Equal(8, spec["itemSpacing"]!.GetValue<double>());
        Assert.Equal(16, spec["paddingLeft"]!.GetValue<double>());
        Assert.Equal(2, spec["strokeWeight"]!.GetValue<double>());
    }

    [Fact]
    public void HugSizedChildGetsNoExplicitDimensions()
    {
        var child = ReconstructionSpec.Extract(Button())["children"]![0]!;

        Assert.Null(child["width"]);
        Assert.Null(child["height"]);
        Assert.Equal("Buy", child["characters"]!.GetValue<string>());
    }

    [Fact]
    public void TextStyleIsReadFromTheRestStyleObject()
    {
        var child = ReconstructionSpec.Extract(Button())["children"]![0]!;

        Assert.Equal(14, child["fontSize"]!.GetValue<double>());
        Assert.Equal("Inter", child["fontName"]!["family"]!.GetValue<string>());
    }

    [Fact]
    public void ValidationPointsAtTheOffendingChild()
    {
        var spec = JsonNode.Parse("""
            {"name":"Root","type":"FRAME","children":[{"type":"TEXT"}]}
            """)!;

        var (valid, errors) = ReconstructionSpec.Validate(spec);

        Assert.False(valid);
        Assert.Contains(errors, e => e.StartsWith("children[0]", StringComparison.Ordinal));
    }

    [Fact]
    public void VariantsAreListedAndExtractableByName()
    {
        var set = JsonNode.Parse("""
            {"id":"1:1","name":"Button","type":"COMPONENT_SET","children":[
              {"id":"1:2","name":"Size=S, State=Default","type":"COMPONENT","width":80,"height":32},
              {"id":"1:3","name":"Size=L, State=Hover","type":"COMPONENT","width":120,"height":48}
            ]}
            """)!;

        Assert.Equal(2, ReconstructionSpec.ListVariants(set).Count);

        var variant = ReconstructionSpec.ExtractVariant(set, "Size=L, State=Hover");
        Assert.Equal(120, variant!["width"]!.GetValue<double>());
        Assert.Null(ReconstructionSpec.ExtractVariant(set, "Size=XXL"));
    }
}

public class StyleExtractorTests
{
    private static JsonNode Screen() => JsonNode.Parse("""
        {
          "id": "0:1", "name": "Screen", "type": "FRAME",
          "cornerRadius": 12,
          "itemSpacing": 15,
          "fills": [{"type":"SOLID","color":{"r":1,"g":1,"b":1,"a":1}}],
          "children": [
            {"id":"1:1","name":"Background","type":"RECTANGLE",
             "fills":[{"type":"SOLID","color":{"r":1,"g":1,"b":1,"a":1}}]},
            {"id":"1:2","name":"Heading","type":"TEXT",
             "style":{"fontFamily":"Inter","fontSize":24,"fontWeight":700,"lineHeightPx":32},
             "fills":[{"type":"SOLID","color":{"r":0,"g":0,"b":0,"a":1}}]},
            {"id":"1:3","name":"Error label","type":"TEXT",
             "style":{"fontFamily":"Inter","fontSize":24,"fontWeight":700,"lineHeightPx":32},
             "fills":[{"type":"SOLID","color":{"r":0.9,"g":0.1,"b":0.1,"a":1}}]},
            {"id":"1:4","name":"Warning banner","type":"RECTANGLE",
             "fills":[{"type":"SOLID","color":{"r":1,"g":0.6,"b":0,"a":1}}]}
          ]
        }
        """)!;

    [Fact]
    public void IdenticalValuesCollapseIntoOneToken()
    {
        var styles = new StyleExtractor().Extract(Screen());

        Assert.Single(styles, s => s.Type == "COLOR" && s.Value == "#FFFFFF");
        Assert.Single(styles, s => s.Type == "TYPOGRAPHY");
    }

    [Fact]
    public void ColorCategoryIsInferredFromTheNodeName()
    {
        var styles = new StyleExtractor().Extract(Screen());

        Assert.Contains(styles, s => s.Type == "COLOR" && s.Value == "#FFFFFF" && s.Category == "background");

        Assert.Contains(styles, s => s.Type == "COLOR" && s.Category == "text");
        Assert.Contains(styles, s => s.Type == "COLOR" && s.Category == "semantic");
    }

    [Fact]
    public void SpacingNameSnapsToTheFourPixelGrid()
    {
        var styles = new StyleExtractor().Extract(Screen());
        var spacing = styles.Single(s => s.Type == "SPACING");

        Assert.Equal("itemSpacing/16", spacing.Name);
        Assert.Equal("15px", spacing.Value);
    }

    [Theory]
    [InlineData(0, "none")]
    [InlineData(2, "xs")]
    [InlineData(8, "md")]
    [InlineData(40, "xxl")]
    public void RadiusIsBucketedIntoNamedSizes(double value, string expected) =>
        Assert.Equal(expected, StyleExtractor.CategorizeRadius(value));

    [Fact]
    public void FormattedOutputCarriesCountsAndTheEnterpriseCaveat()
    {
        var formatted = StyleExtractor.Format(new StyleExtractor().Extract(Screen()));

        Assert.True(formatted["counts"]!["total"]!.GetValue<int>() > 0);
        Assert.Contains("Enterprise", formatted["note"]!.GetValue<string>());
    }
}

public class ParityReportTests
{
    [Fact]
    public void MatchingFieldsRaiseTheScoreAndMismatchesCarryFixes()
    {
        var design = (JsonObject)JsonNode.Parse("""{"fontSize":14,"paddingLeft":16,"cornerRadius":8}""")!;
        var code = (JsonObject)JsonNode.Parse("""{"fontSize":14,"paddingLeft":12,"cornerRadius":8}""")!;

        var report = ParityReport.Compare(design, code);

        Assert.Equal(3, report["comparedFields"]!.GetValue<int>());
        Assert.Equal(2.0 / 3, report["parityScore"]!.GetValue<double>(), 3);

        var mismatch = (report["mismatches"] as JsonArray)!.Single()!;
        Assert.Equal("paddingLeft", mismatch["field"]!.GetValue<string>());
        Assert.Contains("16", mismatch["fixInCode"]!.GetValue<string>());
    }

    [Fact]
    public void SubPixelDifferencesAreNotTreatedAsDiscrepancies()
    {
        var design = (JsonObject)JsonNode.Parse("""{"width":120.3}""")!;
        var code = (JsonObject)JsonNode.Parse("""{"width":120}""")!;

        Assert.Equal(1.0, ParityReport.Compare(design, code)["parityScore"]!.GetValue<double>());
    }

    [Fact]
    public void HexColourInCodeIsComparedAgainstFigmaChannels()
    {
        var design = (JsonObject)JsonNode.Parse("""{"fills":[{"type":"SOLID","color":{"r":1,"g":1,"b":1,"a":1}}]}""")!;

        Assert.Equal(1.0, ParityReport.Compare(design, (JsonObject)JsonNode.Parse("""{"fills":"#FFFFFF"}""")!)["parityScore"]!.GetValue<double>());
        Assert.Equal(1.0, ParityReport.Compare(design, (JsonObject)JsonNode.Parse("""{"fills":"#fff"}""")!)["parityScore"]!.GetValue<double>());
    }

    [Fact]
    public void FieldsAbsentFromTheDesignAreReportedSeparatelyNotAsMismatches()
    {
        var design = (JsonObject)JsonNode.Parse("""{"fontSize":14}""")!;
        var code = (JsonObject)JsonNode.Parse("""{"fontSize":14,"boxShadow":"0 1px 2px"}""")!;

        var report = ParityReport.Compare(design, code);

        Assert.Empty((report["mismatches"] as JsonArray)!);
        Assert.Single((report["notInDesign"] as JsonArray)!);
        Assert.Equal(1.0, report["parityScore"]!.GetValue<double>());
    }
}

public class ComponentDocTests
{
    [Fact]
    public void DocumentCoversAnatomyVariantsPropertiesAndTokens()
    {
        var node = JsonNode.Parse("""
            {
              "id":"1:1","name":"Button","type":"COMPONENT_SET",
              "description":"The primary interface button.",
              "componentPropertyDefinitions":{"Label":{"type":"TEXT","defaultValue":"Button"}},
              "children":[
                {"id":"1:2","name":"Size=S","type":"COMPONENT",
                 "fills":[{"type":"SOLID","color":{"r":0,"g":0,"b":1,"a":1}}],
                 "children":[{"id":"1:3","name":"Label","type":"TEXT",
                   "style":{"fontFamily":"Inter","fontSize":14}}]}
              ]
            }
            """)!;

        var markdown = ComponentDoc.Render(node, new StyleExtractor().Extract(node), null);

        Assert.Contains("# Button", markdown);
        Assert.Contains("The primary interface button.", markdown);
        Assert.Contains("## Contents", markdown);
        Assert.Contains("## Variants", markdown);
        Assert.Contains("`Size=S`", markdown);
        Assert.Contains("## Properties", markdown);
        Assert.Contains("## Colors", markdown);
    }

    [Fact]
    public void CodeInfoBecomesItsOwnSection()
    {
        var node = JsonNode.Parse("""{"id":"1:1","name":"Card","type":"COMPONENT"}""")!;
        var codeInfo = (JsonObject)JsonNode.Parse("""{"source":"src/Card.tsx","props":["title","onClick"]}""")!;

        var markdown = ComponentDoc.Render(node, [], codeInfo);

        Assert.Contains("## From the code side", markdown);
        Assert.Contains("src/Card.tsx", markdown);
    }
}
