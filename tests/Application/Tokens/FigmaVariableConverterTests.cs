using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Application.Tokens;

public class FigmaVariableConverterTests
{
    /// <summary>Variables API response with two modes, an alias and scopes.</summary>
    private static JsonNode VariablesPayload() => JsonNode.Parse("""
        {
          "variableCollections": {
            "VariableCollectionId:1:1": {
              "name": "Core",
              "modes": [
                { "modeId": "1:0", "name": "Light" },
                { "modeId": "1:1", "name": "Dark" }
              ]
            }
          },
          "variables": {
            "VariableID:2:1": {
              "name": "color/blue/500",
              "resolvedType": "COLOR",
              "variableCollectionId": "VariableCollectionId:1:1",
              "description": "Base blue",
              "scopes": ["FILL_COLOR"],
              "codeSyntax": { "WEB": "var(--color-blue-500)" },
              "valuesByMode": {
                "1:0": { "r": 0.25, "g": 0.52, "b": 0.95, "a": 1 },
                "1:1": { "r": 0.12, "g": 0.37, "b": 0.8, "a": 1 }
              }
            },
            "VariableID:2:2": {
              "name": "color/brand",
              "resolvedType": "COLOR",
              "variableCollectionId": "VariableCollectionId:1:1",
              "scopes": ["ALL_SCOPES"],
              "valuesByMode": {
                "1:0": { "type": "VARIABLE_ALIAS", "id": "VariableID:2:1" },
                "1:1": { "type": "VARIABLE_ALIAS", "id": "VariableID:2:1" }
              }
            },
            "VariableID:2:3": {
              "name": "spacing/md",
              "resolvedType": "FLOAT",
              "variableCollectionId": "VariableCollectionId:1:1",
              "valuesByMode": { "1:0": 16, "1:1": 16 }
            }
          }
        }
        """)!;

    /// <summary>The same payload the way the plugin sends it: each section an array, the id moved inside the item.</summary>
    private static JsonNode AsThePluginSendsIt(JsonNode rest)
    {
        static JsonArray Listed(JsonNode section) => new([.. section.AsObject().Select(pair =>
        {
            var item = pair.Value!.DeepClone().AsObject();
            item["id"] = pair.Key;

            return (JsonNode)item;
        })]);

        return new JsonObject
        {
            ["variableCollections"] = Listed(rest["variableCollections"]!),
            ["variables"] = Listed(rest["variables"]!),
        };
    }

    [Fact]
    public void ThePluginShapeConvertsToTheSameTokensAsTheRestShape()
    {
        var fromRest = FigmaVariableConverter.Convert(VariablesPayload());
        var fromPlugin = FigmaVariableConverter.Convert(AsThePluginSendsIt(VariablesPayload()));

        Assert.Equal(fromRest.AllTokens.Select(token => token.DottedPath), fromPlugin.AllTokens.Select(token => token.DottedPath));
        Assert.Equal(["Light", "Dark"], Assert.Single(fromPlugin.Sets).Modes);
        Assert.Equal("color.blue.500", fromPlugin.AllTokens.Single(token => token.DottedPath == "color.brand").Values["Light"].Reference);
    }

    [Fact]
    public void CollectionBecomesASetAndModesKeepTheirNames()
    {
        var document = FigmaVariableConverter.Convert(VariablesPayload());

        var set = Assert.Single(document.Sets);
        Assert.Equal("Core", set.Name);
        Assert.Equal(["Light", "Dark"], set.Modes);
        Assert.Equal(3, set.Tokens.Count);
    }

    [Fact]
    public void SlashedVariableNamesBecomeTokenPaths()
    {
        var document = FigmaVariableConverter.Convert(VariablesPayload());

        var blue = document.AllTokens.Single(t => t.DottedPath == "color.blue.500");
        Assert.Equal(["color", "blue", "500"], blue.Path);
        Assert.Equal(TokenTypes.Color, blue.Type);
        Assert.Equal("Base blue", blue.Description);
    }

    [Fact]
    public void FigmaFloatChannelsBecomeHexPerMode()
    {
        var blue = FigmaVariableConverter.Convert(VariablesPayload())
            .AllTokens.Single(t => t.DottedPath == "color.blue.500");

        Assert.Equal("#4085F2", blue.Values["Light"].Literal!.GetValue<string>());
        Assert.Equal("#1F5ECC", blue.Values["Dark"].Literal!.GetValue<string>());
    }

    [Fact]
    public void VariableAliasesBecomeTokenReferencesNotOpaqueIds()
    {
        var brand = FigmaVariableConverter.Convert(VariablesPayload())
            .AllTokens.Single(t => t.DottedPath == "color.brand");

        Assert.True(brand.Values["Light"].IsReference);
        Assert.Equal("color.blue.500", brand.Values["Light"].Reference);
    }

    [Fact]
    public void FloatVariablesGetPixelsBecauseFigmaStoresNoUnit()
    {
        var spacing = FigmaVariableConverter.Convert(VariablesPayload())
            .AllTokens.Single(t => t.DottedPath == "spacing.md");

        Assert.Equal(TokenTypes.Dimension, spacing.Type);
        Assert.Equal("16px", spacing.Values["Light"].Literal!.GetValue<string>());
    }

    [Fact]
    public void IdentityMetadataSurvivesForRoundTrip()
    {
        var blue = FigmaVariableConverter.Convert(VariablesPayload())
            .AllTokens.Single(t => t.DottedPath == "color.blue.500");

        Assert.Equal("VariableID:2:1", blue.Figma!.VariableId);
        Assert.Equal("VariableCollectionId:1:1", blue.Figma.CollectionId);
        Assert.Equal("COLOR", blue.Figma.FigmaResolvedType);
        Assert.Equal(["FILL_COLOR"], blue.Figma.Scopes!);
        Assert.Equal("var(--color-blue-500)", blue.Figma.CodeSyntax!["WEB"]);
    }

    [Fact]
    public void DefaultScopingIsOmittedSoOutputStaysClean()
    {
        var brand = FigmaVariableConverter.Convert(VariablesPayload())
            .AllTokens.Single(t => t.DottedPath == "color.brand");

        Assert.Null(brand.Figma!.Scopes);
    }

    [Fact]
    public void ModeNamesCanBeRemappedToCodeConventions()
    {
        var document = FigmaVariableConverter.Convert(
            VariablesPayload(),
            new Dictionary<string, string> { ["Light"] = "light", ["Dark"] = "dark" });

        Assert.Equal(["light", "dark"], document.Sets.Single().Modes);
        Assert.True(document.AllTokens.First().Values.ContainsKey("light"));
    }

    [Fact]
    public void UnresolvableAliasKeepsTheIdRatherThanVanishing()
    {
        var payload = JsonNode.Parse("""
            {
              "variableCollections": {},
              "variables": {
                "VariableID:9:1": {
                  "name": "color/orphan",
                  "resolvedType": "COLOR",
                  "valuesByMode": { "1:0": { "type": "VARIABLE_ALIAS", "id": "VariableID:missing" } }
                }
              }
            }
            """)!;

        var token = FigmaVariableConverter.Convert(payload).AllTokens.Single();

        Assert.Contains("unresolved:VariableID:missing", token.Values["1:0"].Literal!.GetValue<string>());
    }

    [Theory]
    [InlineData(TokenTypes.Color, "COLOR")]
    [InlineData(TokenTypes.Dimension, "FLOAT")]
    [InlineData(TokenTypes.Number, "FLOAT")]
    [InlineData(TokenTypes.Boolean, "BOOLEAN")]
    [InlineData(TokenTypes.String, "STRING")]
    public void TokenTypeMapsBackOntoFigmaNativeType(string dtcg, string expected) =>
        Assert.Equal(expected, TokenTools.ToFigmaType(dtcg));

    [Fact]
    public void FigmaVariablesFlowStraightIntoAnyOutputFormat()
    {
        var document = FigmaVariableConverter.Convert(VariablesPayload());
        var css = Snail.MCP.Figma.Application.Tokens.Formatters.TokenFormatters.Find("css-vars")!
            .Write(document, new Snail.MCP.Figma.Application.Tokens.Formatters.FormatOptions()).Files[0].Content;

        Assert.Contains("--color-blue-500: #4085F2;", css);
        Assert.Contains("--color-brand: var(--color-blue-500);", css);
        Assert.Contains("--spacing-md: 16px;", css);
        Assert.Contains(".dark {", css);
    }
}
