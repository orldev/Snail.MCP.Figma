using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Formatters;
using Snail.MCP.Figma.Application.Tokens.Parsers;

namespace Snail.MCP.Figma.Tests.Application.Tokens;

/// <summary>Stage 5 definition of done: nine parsers and eleven formatters share one model.</summary>
public class TokenParserTests
{
    private static TokenDocument Parse(ITokenParser parser, string content, string setName = "Tokens") =>
        parser.Parse(content, new ParseContext(setName, new TokenDocument())).Document;

    [Fact]
    public void TokenParsers_DeclaredFormats_EachHaveAParserAndAFormatter()
    {
        Assert.Equal(9, TokenParsers.Formats.Count);
        Assert.Equal(11, TokenFormatters.Formats.Count);

        foreach (var format in TokenParsers.Formats)
        {
            Assert.NotNull(TokenParsers.Find(format));
        }

        foreach (var format in TokenFormatters.Formats)
        {
            Assert.NotNull(TokenFormatters.Find(format));
        }
    }

    [Fact]
    public void DtcgParser_FullDocument_ReadsTypesGroupsAliasesAndExtensions()
    {
        var document = Parse(new DtcgParser(), """
            {
              "color": {
                "$type": "color",
                "blue": { "$value": "#0000FF", "$description": "Primary blue" },
                "primary": {
                  "$value": "{color.blue}",
                  "$extensions": {
                    "figma-console-mcp": { "variableId": "VariableID:1:2" },
                    "studio.tokens": { "modify": {} }
                  }
                }
              },
              "spacing": { "md": { "$value": "16px", "$type": "dimension" } }
            }
            """);

        var tokens = document.AllTokens.ToArray();
        Assert.Equal(3, tokens.Length);

        var blue = tokens.Single(t => t.DottedPath == "color.blue");
        Assert.Equal(TokenTypes.Color, blue.Type);
        Assert.Equal("Primary blue", blue.Description);

        var primary = tokens.Single(t => t.DottedPath == "color.primary");
        Assert.True(primary.ValueFor(null)!.IsReference);
        Assert.Equal("color.blue", primary.ValueFor(null)!.Reference);
        Assert.Equal("VariableID:1:2", primary.Figma!.VariableId);

        Assert.NotNull(primary.ForeignExtensions?["studio.tokens"]);
    }

    [Fact]
    public void TokensStudioParser_StudioTypes_AreMappedOntoDtcgTypes()
    {
        var document = Parse(new TokensStudioParser(), """
            {
              "colors": { "brand": { "value": "#FF0000", "type": "color" } },
              "sizes": { "sm": { "value": "8px", "type": "sizing" } },
              "fonts": { "body": { "value": "Inter", "type": "fontFamilies" } }
            }
            """);

        var tokens = document.AllTokens.ToArray();
        Assert.Equal(TokenTypes.Color, tokens.Single(t => t.DottedPath == "colors.brand").Type);
        Assert.Equal(TokenTypes.Dimension, tokens.Single(t => t.DottedPath == "sizes.sm").Type);
        Assert.Equal(TokenTypes.FontFamily, tokens.Single(t => t.DottedPath == "fonts.body").Type);
    }

    [Fact]
    public void TokensStudioParser_MetadataDeclaresTokenSets_SplitsIntoThoseSets()
    {
        var document = Parse(new TokensStudioParser(), """
            {
              "$metadata": { "tokenSetOrder": ["primitives", "semantic"] },
              "primitives": { "blue": { "value": "#0000FF", "type": "color" } },
              "semantic": { "brand": { "value": "{blue}", "type": "color" } }
            }
            """);

        Assert.Equal(["primitives", "semantic"], document.Sets.Select(s => s.Name));
        Assert.Equal("blue", document.Sets[1].Tokens.Single().Values["Value"].Reference);
    }

    [Fact]
    public void CssVarsParser_ThemeSelectors_BecomeModes()
    {
        var document = Parse(new CssVarsParser(), """
            :root {
              --color-bg: #FFFFFF;
              --color-fg: var(--color-bg);
              --spacing-md: 16px;
            }
            .dark {
              --color-bg: #000000;
            }
            [data-theme="sepia"] {
              --color-bg: #F5E6C8;
            }
            """);

        var set = document.Sets.Single();
        Assert.Equal(["Value", "Dark", "sepia"], set.Modes);

        var background = set.Tokens.Single(t => t.DottedPath == "color.bg");
        Assert.Equal("#FFFFFF", background.Values["Value"].Literal!.GetValue<string>());
        Assert.Equal("#000000", background.Values["Dark"].Literal!.GetValue<string>());

        Assert.Equal("color.bg", set.Tokens.Single(t => t.DottedPath == "color.fg").Values["Value"].Reference);
    }

    [Fact]
    public void ScssParser_VariablesAndSassMaps_ReadsVariablesAndSkipsMaps()
    {
        var result = new ScssParser().Parse("""
            // comment
            $color-primary: #FF0000;
            $color-alias: $color-primary;
            $spacing: (small: 4px, large: 16px);
            """, new ParseContext("Tokens", new TokenDocument()));

        var tokens = result.Document.AllTokens.ToArray();
        Assert.Equal(2, tokens.Length);
        Assert.Equal("color.primary", tokens[1].Values["Value"].Reference);
        Assert.Contains(result.Warnings, w => w.Contains("Sass maps", StringComparison.Ordinal));
    }

    [Fact]
    public void TailwindV4Parser_ThemeBlock_BecomesTokens()
    {
        var document = Parse(new TailwindV4Parser(), """
            @import "tailwindcss";

            @theme {
              --color-brand: #4085F2;
              --spacing-lg: 24px;
            }
            """);

        Assert.Equal(2, document.TokenCount);
        Assert.Contains(document.AllTokens, t => t.DottedPath == "color.brand");
    }

    [Fact]
    public void TailwindV3Parser_ConfigObject_BecomesTokens()
    {
        var document = Parse(new TailwindV3Parser(), """
            module.exports = {
              theme: {
                extend: {
                  colors: {
                    brand: { DEFAULT: '#4085F2', dark: '#2C5FB8' },
                  },
                  spacing: { lg: '24px' },
                },
              },
            };
            """);

        var paths = document.AllTokens.Select(t => t.DottedPath).ToArray();

        Assert.Contains("color.brand", paths);
        Assert.Contains("color.brand.dark", paths);
        Assert.Contains("spacing.lg", paths);
    }

    [Fact]
    public void StyleDictionaryV3Parser_References_LoseTheirValueSuffix()
    {
        var document = Parse(new StyleDictionaryV3Parser(), """
            {
              "color": {
                "base": { "value": "#FF0000", "type": "color" },
                "brand": { "value": "{color.base.value}", "type": "color" }
              }
            }
            """);

        Assert.Equal("color.base", document.AllTokens.Single(t => t.DottedPath == "color.brand").Values["Value"].Reference);
    }

    [Fact]
    public void TokenParsers_FlatAndNestedJson_ProduceTheSamePaths()
    {
        var flat = Parse(new JsonFlatParser(), """{"color.brand":"#FF0000","spacing.md":"16px"}""");
        var nested = Parse(new JsonNestedParser(), """{"color":{"brand":"#FF0000"},"spacing":{"md":"16px"}}""");

        Assert.Equal(
            flat.AllTokens.Select(t => t.DottedPath).OrderBy(p => p, StringComparer.Ordinal),
            nested.AllTokens.Select(t => t.DottedPath).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void TokenParsers_FileName_GuessesTheFormat()
    {
        Assert.Equal("dtcg", TokenParsers.GuessFormat("design.tokens.json"));
        Assert.Equal("css-vars", TokenParsers.GuessFormat("src/styles/tokens.css"));
        Assert.Equal("scss", TokenParsers.GuessFormat("_variables.scss"));
        Assert.Equal("tailwind-v3", TokenParsers.GuessFormat("tailwind.config.js"));
    }

    [Fact]
    public void DtcgParser_BrokenInput_ReturnsWarningInsteadOfThrowing()
    {
        var result = new DtcgParser().Parse("{not json", new ParseContext("Tokens", new TokenDocument()));

        Assert.Empty(result.Document.AllTokens);
        Assert.Single(result.Warnings);
    }
}

public class AliasResolverTests
{
    private static TokenDocument Chain()
    {
        var document = new TokenDocument();
        var set = document.GetOrAddSet("Tokens");
        set.Modes.Add("Value");

        var baseToken = new Token { Path = ["color", "base"], Type = TokenTypes.Color };
        baseToken.Values["Value"] = TokenValue.FromLiteral("#FF0000");

        var middle = new Token { Path = ["color", "brand"], Type = TokenTypes.Color };
        middle.Values["Value"] = TokenValue.FromReference("color.base");

        var top = new Token { Path = ["color", "button"], Type = TokenTypes.Color };
        top.Values["Value"] = TokenValue.FromReference("color.brand");

        set.Tokens.AddRange([baseToken, middle, top]);

        return document;
    }

    [Fact]
    public void AliasResolver_AliasChain_ResolvesDownToTheLiteral()
    {
        var document = Chain();
        var resolver = new AliasResolver(document);
        var top = document.AllTokens.Single(t => t.DottedPath == "color.button");

        Assert.Equal("#FF0000", resolver.ResolveToLiteral(top, "Value")!.Literal!.GetValue<string>());
    }

    [Fact]
    public void AliasResolver_TargetMissing_IsReportedRatherThanSilentlyDropped()
    {
        var document = new TokenDocument();
        var set = document.GetOrAddSet("Tokens");
        set.Modes.Add("Value");

        var orphan = new Token { Path = ["color", "x"], Type = TokenTypes.Color };
        orphan.Values["Value"] = TokenValue.FromReference("color.nope");
        set.Tokens.Add(orphan);

        var resolver = new AliasResolver(document);

        Assert.Null(resolver.ResolveToLiteral(orphan, "Value"));
        Assert.Contains(resolver.Warnings, w => w.Contains("leads nowhere", StringComparison.Ordinal));
    }

    [Fact]
    public void AliasResolver_ReferenceCycle_IsBrokenWithAWarningInsteadOfHanging()
    {
        var document = new TokenDocument();
        var set = document.GetOrAddSet("Tokens");
        set.Modes.Add("Value");

        var a = new Token { Path = ["a"], Type = TokenTypes.Color };
        a.Values["Value"] = TokenValue.FromReference("b");
        var b = new Token { Path = ["b"], Type = TokenTypes.Color };
        b.Values["Value"] = TokenValue.FromReference("a");
        set.Tokens.AddRange([a, b]);

        var resolver = new AliasResolver(document);

        Assert.Null(resolver.ResolveToLiteral(a, "Value"));
        Assert.Contains(resolver.Warnings, w => w.Contains("loop", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{color.primary}", "color.primary")]
    [InlineData("{color/primary}", "color.primary")]
    public void AliasResolver_DottedOrSlashedReference_IsAccepted(string input, string expected)
    {
        Assert.True(AliasResolver.TryParseReference(input, out var path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("var(--color-primary)")]
    [InlineData("text {with braces} inside")]
    public void AliasResolver_PartialBraces_AreNotReferences(string input) =>
        Assert.False(AliasResolver.TryParseReference(input, out _));
}

/// <summary>Multi-set DTCG goes under separate root keys and references must account for them, or aliases dangle after re-read.</summary>
public class DtcgMultiSetAliasTests
{
    private static TokenDocument TwoSetsWithACrossReference()
    {
        var document = new TokenDocument();

        var primitives = document.GetOrAddSet("Primitives");
        primitives.Modes.Add("Default");
        primitives.Tokens.Add(new Token
        {
            Path = ["color", "brand", "500"],
            Type = TokenTypes.Color,
            Values = { ["Default"] = TokenValue.FromLiteral("#3b82f6") },
        });

        var semantic = document.GetOrAddSet("Semantic");
        semantic.Modes.Add("Default");
        semantic.Tokens.Add(new Token
        {
            Path = ["surface"],
            Type = TokenTypes.Color,
            Values = { ["Default"] = TokenValue.FromReference("color.brand.500") },
        });

        return document;
    }

    [Fact]
    public void DtcgFormatter_CrossSetReference_IsQualifiedWithItsSetKey()
    {
        var written = new DtcgFormatter().Write(TwoSetsWithACrossReference(), new FormatOptions()).Files.Single();

        Assert.Contains("{Primitives.color.brand.500}", written.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void DtcgFormatter_MultiSetOutputParsedBack_EveryAliasStillResolves()
    {
        var written = new DtcgFormatter().Write(TwoSetsWithACrossReference(), new FormatOptions()).Files.Single();
        var reparsed = new DtcgParser()
            .Parse(written.Content, new ParseContext("Tokens", new TokenDocument()))
            .Document;

        var resolver = new AliasResolver(reparsed);

        foreach (var token in reparsed.AllTokens)
        {
            foreach (var value in token.Values.Values.Where(v => v.IsReference))
            {
                Assert.NotNull(resolver.Resolve(value.Reference!));
            }
        }
    }

    [Fact]
    public void DtcgFormatter_SingleSetDocument_KeepsItsReferencesUnqualified()
    {
        var document = new TokenDocument();
        var set = document.GetOrAddSet("Tokens");
        set.Modes.Add("Default");
        set.Tokens.Add(new Token
        {
            Path = ["color", "brand"],
            Type = TokenTypes.Color,
            Values = { ["Default"] = TokenValue.FromLiteral("#3b82f6") },
        });
        set.Tokens.Add(new Token
        {
            Path = ["surface"],
            Type = TokenTypes.Color,
            Values = { ["Default"] = TokenValue.FromReference("color.brand") },
        });

        var written = new DtcgFormatter().Write(document, new FormatOptions()).Files.Single();

        Assert.Contains("{color.brand}", written.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("{Tokens.color.brand}", written.Content, StringComparison.Ordinal);
    }
}
