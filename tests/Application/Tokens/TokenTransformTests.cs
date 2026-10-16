using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Transforms;

namespace Snail.MCP.Figma.Tests.Application.Tokens;

public class ColorTransformTests
{
    [Theory]
    [InlineData("#FF0000", 255, 0, 0, 1)]
    [InlineData("#f00", 255, 0, 0, 1)]
    [InlineData("#FF000080", 255, 0, 0, 0.502)]
    [InlineData("rgb(255, 0, 0)", 255, 0, 0, 1)]
    [InlineData("rgba(255, 0, 0, 0.5)", 255, 0, 0, 0.5)]
    public void ColorTransform_CommonNotation_IsParsed(string input, int r, int g, int b, double a)
    {
        var color = ColorTransform.Parse(input);

        Assert.NotNull(color);
        Assert.Equal(r, color!.Value.R255);
        Assert.Equal(g, color.Value.G255);
        Assert.Equal(b, color.Value.B255);
        Assert.Equal(a, color.Value.A, 2);
    }

    [Theory]
    [InlineData("not a color")]
    [InlineData("#12345")]
    [InlineData("")]
    public void ColorTransform_GarbageInput_IsRejectedInsteadOfGuessed(string input) => Assert.Null(ColorTransform.Parse(input));

    [Fact]
    public void ColorTransform_HexWithTransparency_FallsBackToRgba()
    {
        Assert.Equal("rgba(255, 0, 0, 0.5)", ColorTransform.Convert("rgba(255,0,0,0.5)", "hex"));
        Assert.Equal("#FF0000", ColorTransform.Convert("rgb(255,0,0)", "hex"));
    }

    [Theory]
    [InlineData("hex8", "#FF000080")]
    [InlineData("rgba", "rgba(255, 0, 0, 0.502)")]
    public void ColorTransform_NotationRequested_EmitsThatNotation(string format, string expected) =>
        Assert.Equal(expected, ColorTransform.Convert("#FF000080", format));

    [Fact]
    public void ColorTransform_HslOrOklchRequested_ConvertsToIt()
    {
        Assert.Equal("hsl(0 100% 50%)", ColorTransform.Convert("#FF0000", "hsl"));

        var oklch = ColorTransform.Convert("#FF0000", "oklch");
        Assert.StartsWith("oklch(62.8%", oklch);
    }

    [Fact]
    public void ColorTransform_UnparseableValue_PassesThroughUnchanged() =>
        Assert.Equal("currentColor", ColorTransform.Convert("currentColor", "rgba"));
}

public class SizeTransformTests
{
    [Theory]
    [InlineData("16px", "rem", "1rem")]
    [InlineData("1rem", "px", "16px")]
    [InlineData("12pt", "px", "16px")]
    [InlineData("24", "rem", "1.5rem")]
    public void SizeTransform_TargetUnit_ConvertsBetweenUnits(string input, string target, string expected) =>
        Assert.Equal(expected, SizeTransform.Convert(input, target));

    [Fact]
    public void SizeTransform_CustomRemBase_IsRespected() =>
        Assert.Equal("2rem", SizeTransform.Convert("20px", "rem", remBase: 10));

    [Fact]
    public void SizeTransform_ZeroValue_LosesItsUnit() =>
        Assert.Equal("0", SizeTransform.Convert("0px", "rem"));

    [Theory]
    [InlineData("50%")]
    [InlineData("auto")]
    public void SizeTransform_UnconvertibleValue_IsLeftAlone(string input) =>
        Assert.Equal(input, SizeTransform.Convert(input, "px"));
}

public class TokenNamingTests
{
    [Fact]
    public void TokenNaming_CssName_SlugifiesEverySegment() =>
        Assert.Equal("tailwind-colors-purple-50",
            TokenNaming.ToCssName(["Tailwind Colors", "purple", "50"]));

    [Fact]
    public void TokenNaming_CamelName_IsAValidJavaScriptIdentifier()
    {
        Assert.Equal("colorBrandPrimary", TokenNaming.ToCamel(["color", "brand", "primary"]));

        Assert.Equal("_50Percent", TokenNaming.ToCamel(["50", "percent"]));
    }

    [Fact]
    public void TokenNaming_KebabFromCamelCase_SplitsSubfields() =>
        Assert.Equal("font-size", TokenNaming.ToKebab("fontSize"));
}
