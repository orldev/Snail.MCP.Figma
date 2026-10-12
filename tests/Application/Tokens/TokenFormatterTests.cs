using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Formatters;
using Snail.MCP.Figma.Application.Tokens.Parsers;

namespace Snail.MCP.Figma.Tests.Application.Tokens;

public class TokenFormatterTests
{
    /// <summary>Document with an alias, a multi-mode token, typography and a shadow — everything that breaks differently.</summary>
    private static TokenDocument Sample()
    {
        var document = new TokenDocument();
        var set = document.GetOrAddSet("Core");
        set.Modes.AddRange(["Light", "Dark"]);

        var blue = new Token { Path = ["color", "blue"], Type = TokenTypes.Color, Description = "Base blue" };
        blue.Values["Light"] = TokenValue.FromLiteral("#4085F2");
        blue.Values["Dark"] = TokenValue.FromLiteral("#1E5FCC");

        var brand = new Token { Path = ["color", "brand"], Type = TokenTypes.Color };
        brand.Values["Light"] = TokenValue.FromReference("color.blue");
        brand.Values["Dark"] = TokenValue.FromReference("color.blue");

        var spacing = new Token { Path = ["spacing", "md"], Type = TokenTypes.Dimension };
        spacing.Values["Light"] = TokenValue.FromLiteral("16px");
        spacing.Values["Dark"] = TokenValue.FromLiteral("16px");

        var heading = new Token { Path = ["typography", "heading"], Type = TokenTypes.Typography };
        heading.Values["Light"] = TokenValue.FromLiteral(JsonNode.Parse("""
            {"fontFamily":"Inter","fontSize":"24px","fontWeight":700,"lineHeight":"32px"}
            """));

        var shadow = new Token { Path = ["shadow", "card"], Type = TokenTypes.Shadow };
        shadow.Values["Light"] = TokenValue.FromLiteral(JsonNode.Parse("""
            {"color":"#00000033","offsetX":0,"offsetY":2,"blur":8}
            """));

        set.Tokens.AddRange([blue, brand, spacing, heading, shadow]);

        return document;
    }

    private static string Render(string format, FormatOptions? options = null) =>
        TokenFormatters.Find(format)!.Write(Sample(), options ?? new FormatOptions()).Files[0].Content;

    [Fact]
    public void CssKeepsAliasesAsVarReferencesSoTheCascadeStillWorks()
    {
        var css = Render("css-vars");

        Assert.Contains("--color-blue: #4085F2;", css);
        Assert.Contains("--color-brand: var(--color-blue);", css);
    }

    [Fact]
    public void CssMapsModesOntoConventionalSelectors()
    {
        var css = Render("css-vars");

        Assert.Contains(":root {", css);
        Assert.Contains(".dark {", css);
        Assert.Contains("--color-blue: #1E5FCC;", css);
    }

    [Fact]
    public void CssExpandsCompositeTokensIntoPrimitiveProperties()
    {
        var css = Render("css-vars");

        Assert.Contains("--typography-heading-font-family: Inter;", css);
        Assert.Contains("--typography-heading-font-size: 24px;", css);
        Assert.Contains("--shadow-card: 0 2px 8px rgba(0, 0, 0, 0.2);", css);
    }

    [Fact]
    public void PrefixReachesEveryEmittedName()
    {
        var css = Render("css-vars", new FormatOptions { Prefix = "ds-" });

        Assert.Contains("--ds-color-blue:", css);
        Assert.Contains("--ds-color-brand: var(--ds-color-blue);", css);
    }

    [Fact]
    public void SplitByModeProducesOneFilePerMode()
    {
        var result = TokenFormatters.Find("css-vars")!.Write(Sample(), new FormatOptions { ShouldSplitByMode = true });

        Assert.Equal(2, result.Files.Count);
        Assert.Contains(result.Files, f => f.Path == "tokens-light.css");
        Assert.Contains(result.Files, f => f.Path == "tokens-dark.css");
    }

    [Fact]
    public void DtcgRoundTripsTypesDescriptionsAliasesAndExtraModes()
    {
        var dtcg = Render("dtcg");
        var root = JsonNode.Parse(dtcg)!;

        var blue = root["color"]!["blue"]!;
        Assert.Equal("color", blue["$type"]!.GetValue<string>());
        Assert.Equal("#4085F2", blue["$value"]!.GetValue<string>());
        Assert.Equal("Base blue", blue["$description"]!.GetValue<string>());

        var modes = blue["$extensions"]!["figma-console-mcp"]!["modes"]!;
        Assert.Equal("#1E5FCC", modes["Dark"]!.GetValue<string>());

        Assert.Equal("{color.blue}", root["color"]!["brand"]!["$value"]!.GetValue<string>());
    }

    [Fact]
    public void TailwindV3ResolvesAliasesBecauseTheConfigCannotExpressThem()
    {
        var config = Render("tailwind-v3");

        Assert.Contains("'color-brand': '#4085F2'", config);
        Assert.Contains("colors: {", config);
        Assert.Contains("spacing: {", config);
    }

    [Fact]
    public void TailwindV4EmitsAThemeBlock()
    {
        var css = Render("tailwind-v4");

        Assert.Contains("@theme {", css);
        Assert.Contains("--color-blue: #4085F2;", css);
        Assert.Contains(".dark {", css);
    }

    [Fact]
    public void ScssAndLessUseTheirOwnVariableSyntax()
    {
        Assert.Contains("$color-brand: $color-blue;", Render("scss"));
        Assert.Contains("@color-brand: @color-blue;", Render("less"));
    }

    [Fact]
    public void TsModuleProducesValidIdentifiersAndAConstAssertion()
    {
        var ts = Render("ts-module");

        Assert.Contains("export const tokens = {", ts);
        Assert.Contains("colorBlue: '#4085F2',", ts);
        Assert.Contains("colorBrand: '#4085F2',", ts);
        Assert.Contains("} as const;", ts);
    }

    [Fact]
    public void StyleDictionaryAppendsTheValueSuffixToReferences()
    {
        var json = JsonNode.Parse(Render("style-dictionary-v3"))!;

        Assert.Equal("#4085F2", json["color"]!["blue"]!["value"]!.GetValue<string>());
        Assert.Equal("{color.blue.value}", json["color"]!["brand"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void FlatAndNestedJsonDifferOnlyInShape()
    {
        var flat = JsonNode.Parse(Render("json-flat"))!;
        var nested = JsonNode.Parse(Render("json-nested"))!;

        Assert.Equal("#4085F2", flat["color.blue"]!.GetValue<string>());
        Assert.Equal("#4085F2", nested["color"]!["blue"]!.GetValue<string>());
    }

    [Fact]
    public void TokensStudioOutputUsesItsOwnTypeNames()
    {
        var json = JsonNode.Parse(Render("tokens-studio"))!;

        Assert.Equal("color", json["color"]!["blue"]!["type"]!.GetValue<string>());
        Assert.Equal("sizing", json["spacing"]!["md"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void ColorAndSizeTransformsApplyAcrossFormats()
    {
        var css = Render("css-vars", new FormatOptions { ColorFormat = "rgba", SizeUnit = "rem" });

        Assert.Contains("--color-blue: rgb(64, 133, 242);", css);
        Assert.Contains("--spacing-md: 1rem;", css);
    }

    [Fact]
    public void FormatsThatCannotExpressAliasesAreForcedToResolveThem()
    {
        Assert.False(TokenFormatters.SupportsAliases("tailwind-v3"));
        Assert.False(TokenFormatters.SupportsAliases("ts-module"));
        Assert.True(TokenFormatters.SupportsAliases("dtcg"));
        Assert.True(TokenFormatters.SupportsAliases("css-vars"));
    }
}

/// <summary>Round trip: whatever a formatter writes must come back through the parser.</summary>
public class TokenRoundTripTests
{
    private static TokenDocument Source()
    {
        var document = new TokenDocument();
        var set = document.GetOrAddSet("Core");
        set.Modes.Add("Value");

        var blue = new Token { Path = ["color", "blue"], Type = TokenTypes.Color, Description = "Base blue" };
        blue.Values["Value"] = TokenValue.FromLiteral("#4085F2");

        var brand = new Token { Path = ["color", "brand"], Type = TokenTypes.Color };
        brand.Values["Value"] = TokenValue.FromReference("color.blue");

        var spacing = new Token { Path = ["spacing", "md"], Type = TokenTypes.Dimension };
        spacing.Values["Value"] = TokenValue.FromLiteral("16px");

        set.Tokens.AddRange([blue, brand, spacing]);

        return document;
    }

    [Theory]
    [InlineData("dtcg", "dtcg")]
    [InlineData("css-vars", "css-vars")]
    [InlineData("scss", "scss")]
    [InlineData("json-flat", "json-flat")]
    [InlineData("json-nested", "json-nested")]
    [InlineData("style-dictionary-v3", "style-dictionary-v3")]
    [InlineData("tokens-studio", "tokens-studio")]
    [InlineData("tailwind-v4", "tailwind-v4")]
    public void PathsAndAliasesSurviveTheRoundTrip(string formatterName, string parserName)
    {
        var written = TokenFormatters.Find(formatterName)!.Write(Source(), new FormatOptions()).Files[0];
        var parsed = TokenParsers.Find(parserName)!
            .Parse(written.Content, new ParseContext("Core", new TokenDocument())).Document;

        var paths = parsed.AllTokens.Select(t => t.DottedPath).ToArray();
        Assert.Contains("color.blue", paths);
        Assert.Contains("color.brand", paths);
        Assert.Contains("spacing.md", paths);

        var brand = parsed.AllTokens.Single(t => t.DottedPath == "color.brand");
        Assert.True(brand.ValueFor(null)!.IsReference, $"{formatterName}: the reference did not survive the round trip");
        Assert.Equal("color.blue", brand.ValueFor(null)!.Reference);
    }

    [Fact]
    public void DtcgRoundTripAlsoKeepsTypesAndDescriptions()
    {
        var written = new DtcgFormatter().Write(Source(), new FormatOptions()).Files[0];
        var parsed = new DtcgParser().Parse(written.Content, new ParseContext("Core", new TokenDocument())).Document;

        var blue = parsed.AllTokens.Single(t => t.DottedPath == "color.blue");
        Assert.Equal(TokenTypes.Color, blue.Type);
        Assert.Equal("Base blue", blue.Description);
        Assert.Equal(TokenTypes.Dimension, parsed.AllTokens.Single(t => t.DottedPath == "spacing.md").Type);
    }
}
