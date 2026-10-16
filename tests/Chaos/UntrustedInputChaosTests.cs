using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Application.Extraction;
using Snail.MCP.Figma.Application.Extraction.Codebase;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Parsers;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Chaos;

/// <summary>Input the server trusts although nobody vouches for it: a plugin reply, a manifest committed to a repository, a user's token file, a design file's own data.</summary>
public class UntrustedInputChaosTests
{
    /// <summary>Breaks the screenshot folder: the file name takes <c>format</c> from the plugin's reply verbatim, and FileStream resolves <c>..</c> lexically, so a reply with <c>png/../../escape</c> writes outside the folder.</summary>
    /// <remarks>Any page in a sandboxed iframe (Origin <c>null</c>) or any local process can connect to the bridge and answer CAPTURE_SCREENSHOT.</remarks>
    [Fact]
    public void CaptureStore_FormatClimbsOutOfTheFolder_RefusesTheReply()
    {
        var config = new ServerConfig { DataDirectory = TemporaryFolder.Create() };
        var store = new CaptureStore(config, NullLogger<CaptureStore>.Instance);
        var reply = new JsonObject { ["base64"] = "iVBORw0KGgo=", ["format"] = "png/../../escape" };

        var refusal = Record.Exception(() => store.SaveAndReplace(reply));

        Assert.IsType<InvalidOperationException>(refusal);
        Assert.False(File.Exists(Path.Combine(config.DataDirectory, "escape")));
    }

    /// <summary>Breaks the boundary of figma_ds_extract_component, a tool marked read-only: the component's file comes from analysis.json, which a cloned repository may carry, and an absolute or climbing path reads any file of the user into the model's context.</summary>
    [Theory]
    [InlineData("climbing")]
    [InlineData("absolute")]
    [InlineData("through a link")]
    public async Task DesignSystemExtraction_ManifestPointsOutsideTheRoot_RefusesToReadIt(string route)
    {
        var workspace = TemporaryFolder.Create();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "app")).FullName;
        var outDir = Path.Combine(workspace, "design-system");
        var secret = Path.Combine(workspace, "secret.txt");
        await File.WriteAllTextAsync(secret, "S3CRET-TOKEN");
        Directory.CreateSymbolicLink(Path.Combine(root, "link"), workspace);

        var file = route switch
        {
            "climbing" => "../secret.txt",
            "absolute" => secret,
            _ => "link/secret.txt",
        };

        var analysis = new DsAnalysis
        {
            OutDir = outDir,
            Targets = [new DsTargetAnalysis { Root = root }],
            Components = [new DsComponentEntry { Name = "Evil", File = file, Framework = "react", Classification = DsClassifications.Bespoke }],
        };
        RealExtraction.Manifests.Write(ExtractionManifests.AnalysisPath(outDir), analysis);

        var answer = await RealExtraction.Tools().ExtractComponentAsync(outDir, "Evil");

        Assert.DoesNotContain("S3CRET-TOKEN", answer, StringComparison.Ordinal);
    }

    /// <summary>Breaks token export: every FLOAT variable becomes a dimension, so a font weight of 700 is exported as <c>700px</c> although its scope says FONT_WEIGHT.</summary>
    [Fact]
    public void FigmaVariableConverter_FontWeightVariable_IsNotADimension()
    {
        var variables = JsonNode.Parse("""
            {
              "variableCollections": { "c": { "id": "c", "name": "Type", "modes": [ { "modeId": "m", "name": "Value" } ] } },
              "variables": { "v": { "id": "v", "name": "font/weight/bold", "resolvedType": "FLOAT", "scopes": ["FONT_WEIGHT"],
                                    "variableCollectionId": "c", "valuesByMode": { "m": 700 } } }
            }
            """)!;

        var token = Assert.Single(FigmaVariableConverter.Convert(variables).AllTokens);

        Assert.NotEqual(TokenTypes.Dimension, token.Type);
    }

    /// <summary>Breaks the node budget: a VARIABLE_ALIAS inside boundVariables has an id and a type, so it is counted as a node, and three bound frames under a budget of four lose two of themselves.</summary>
    [Fact]
    public void NodeSanitizer_BoundVariables_DoNotSpendTheNodeBudget()
    {
        static JsonObject Alias(string id) => new() { ["type"] = "VARIABLE_ALIAS", ["id"] = id };

        static JsonObject Frame(int index) => new()
        {
            ["id"] = $"1:{index}",
            ["name"] = $"Card {index}",
            ["type"] = "FRAME",
            ["boundVariables"] = new JsonObject { ["itemSpacing"] = Alias("VariableID:1"), ["paddingLeft"] = Alias("VariableID:2") },
        };

        var root = new JsonObject { ["id"] = "0:1", ["name"] = "Page", ["type"] = "FRAME", ["children"] = new JsonArray(Frame(1), Frame(2), Frame(3)) };
        var sanitizer = new NodeSanitizer(new SanitizationOptions { LogMetrics = false }, NullLogger<NodeSanitizer>.Instance);

        var result = sanitizer.Sanitize(root, new SanitizationRequest { MaxNodes = 4, ShouldDeduplicateStyles = false });

        Assert.Equal(3, result.Node["children"]!.AsArray().Count);
    }

    /// <summary>Breaks the Tailwind import: one key the converter does not reduce to JSON — a numeric spacing key, a reference only JavaScript resolves — throws JsonException past every guard instead of coming back as a warning.</summary>
    [Theory]
    [InlineData("module.exports = { theme: { extend: { spacing: { 128: '32rem' } } } }")]
    [InlineData("module.exports = { theme: { extend: { colors: { brand: colors.indigo } } } }")]
    public void TailwindV3Parser_KeyOnlyJavaScriptReads_ReturnsWarningInsteadOfThrowing(string config)
    {
        var exception = Record.Exception(() => new TailwindV3Parser().Parse(config, new ParseContext("Tailwind", new TokenDocument())));

        Assert.Null(exception);
    }

    /// <summary>Breaks shadows in the design system kit: RgbaToHex drops alpha, and for an effect alpha is where the opacity lives, so a 25 % shadow comes out solid black.</summary>
    [Fact]
    public void DesignSystemKit_TranslucentShadowColor_KeepsItsAlpha()
    {
        var shadow = new JsonObject { ["r"] = 0.0, ["g"] = 0.0, ["b"] = 0.0, ["a"] = 0.25 };

        var hex = DesignSystemKit.RgbaToHex(shadow);

        Assert.NotEqual("#000000", hex);
    }
}
