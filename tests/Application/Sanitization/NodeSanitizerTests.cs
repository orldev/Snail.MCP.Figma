using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Application.Sanitization;

public class NodeSanitizerTests
{
    private static NodeSanitizer Create(SanitizationOptions? options = null) =>
        new(options ?? new SanitizationOptions { LogMetrics = false }, NullLogger<NodeSanitizer>.Instance);

    /// <summary>Node resembling a real Figma response: geometry, defaults and repeated fills.</summary>
    private static JsonNode SampleTree(int leaves = 8)
    {
        var children = new JsonArray();

        for (var i = 0; i < leaves; i++)
        {
            children.Add(new JsonObject
            {
                ["id"] = $"1:{i}",
                ["name"] = $"Vector {i}",
                ["type"] = "VECTOR",
                ["visible"] = true,
                ["locked"] = false,
                ["opacity"] = 1.0,
                ["blendMode"] = "PASS_THROUGH",
                ["exportSettings"] = new JsonArray(),
                ["absoluteBoundingBox"] = new JsonObject { ["x"] = i, ["y"] = 0, ["width"] = 10, ["height"] = 10 },
                ["absoluteRenderBounds"] = new JsonObject { ["x"] = i, ["y"] = 0, ["width"] = 10, ["height"] = 10 },
                ["fillGeometry"] = new JsonArray(new JsonObject { ["path"] = new string('M', 400) }),
                ["vectorPaths"] = new JsonArray(new JsonObject { ["data"] = new string('C', 400) }),
                ["fills"] = new JsonArray(new JsonObject
                {
                    ["type"] = "SOLID",
                    ["blendMode"] = "NORMAL",
                    ["color"] = new JsonObject { ["r"] = 0.1, ["g"] = 0.2, ["b"] = 0.3, ["a"] = 1.0 },
                }),
            });
        }

        return new JsonObject
        {
            ["id"] = "0:1",
            ["name"] = "Page",
            ["type"] = "CANVAS",
            ["clipsContent"] = true,
            ["children"] = new JsonArray(new JsonObject
            {
                ["id"] = "1:100",
                ["name"] = "Frame",
                ["type"] = "FRAME",
                ["children"] = children,
            }),
        };
    }

    [Fact]
    public void Aggressive_CutsGeometryDefaultsAndEmptyArrays()
    {
        var result = Create().Sanitize(SampleTree(), new SanitizationRequest { Profile = SanitizationProfile.Aggressive });

        var json = result.Node.ToJsonString();
        Assert.DoesNotContain("fillGeometry", json);
        Assert.DoesNotContain("vectorPaths", json);
        Assert.DoesNotContain("absoluteBoundingBox", json);
        Assert.DoesNotContain("absoluteRenderBounds", json);
        Assert.DoesNotContain("exportSettings", json);
        Assert.DoesNotContain("\"locked\"", json);
        Assert.DoesNotContain("\"clipsContent\"", json);

        Assert.Contains("\"id\":\"1:100\"", json);
        Assert.Contains("\"type\":\"FRAME\"", json);
    }

    [Fact]
    public void Aggressive_ReachesTheAdvertisedSaving()
    {
        var result = Create().Sanitize(SampleTree(20), new SanitizationRequest { Profile = SanitizationProfile.Aggressive });

        Assert.True(result.Ratio >= 0.70, $"savings were only {result.Ratio:P1}");
        Assert.True(result.BytesOut < result.BytesIn);
    }

    [Fact]
    public void Balanced_KeepsAbsoluteBoundsButDropsVectorGeometry()
    {
        var result = Create().Sanitize(SampleTree(), new SanitizationRequest { Profile = SanitizationProfile.Balanced });

        var json = result.Node.ToJsonString();
        Assert.DoesNotContain("fillGeometry", json);
        Assert.Contains("absoluteBoundingBox", json);
        Assert.Contains("\"locked\"", json);
    }

    [Fact]
    public void Raw_ChangesNothing()
    {
        var source = SampleTree();
        var result = Create().Sanitize(source, new SanitizationRequest { Profile = SanitizationProfile.Raw });

        Assert.True(JsonNode.DeepEquals(source, result.Node));
        Assert.Equal(result.BytesIn, result.BytesOut);
        Assert.Equal(0, result.Ratio);
    }

    [Fact]
    public void MaxDepth_StopsDescentAndReportsWhatWasSkipped()
    {
        var result = Create().Sanitize(SampleTree(), new SanitizationRequest
        {
            Profile = SanitizationProfile.Aggressive,
            MaxDepth = 1,
        });

        var frame = result.Node["children"]![0]!;
        Assert.Null(frame["children"]);
        Assert.Equal(8, frame["childrenOmitted"]!.GetValue<int>());
        Assert.Equal("maxDepth", frame["childrenOmittedReason"]!.GetValue<string>());
        Assert.Equal(8, result.NodesOmitted);
    }

    [Fact]
    public void MaxNodes_CapsThePayload()
    {
        var result = Create().Sanitize(SampleTree(20), new SanitizationRequest
        {
            Profile = SanitizationProfile.Aggressive,
            MaxNodes = 5,
        });

        Assert.True(result.NodesEmitted <= 6, $"nodes in response: {result.NodesEmitted}");
        Assert.True(result.NodesOmitted > 0);
    }

    [Fact]
    public void RepeatedFills_AreHoistedIntoStyleDictionary()
    {
        var result = Create().Sanitize(SampleTree(6), new SanitizationRequest
        {
            Profile = SanitizationProfile.Aggressive,
            ShouldDeduplicateStyles = true,
        });

        Assert.Equal(1, result.StylesDeduplicated);

        var styles = result.Node["$styles"] as JsonObject;
        Assert.NotNull(styles);
        Assert.Single(styles!);

        var firstLeaf = result.Node["children"]![0]!["children"]![0]!;
        Assert.Equal("$styles.s1", firstLeaf["fills"]!.GetValue<string>());
    }

    [Fact]
    public void UniqueStyles_AreLeftInPlace()
    {
        var tree = new JsonObject
        {
            ["id"] = "0:1",
            ["type"] = "FRAME",
            ["fills"] = new JsonArray(new JsonObject
            {
                ["type"] = "SOLID",
                ["color"] = new JsonObject { ["r"] = 1.0, ["g"] = 0.0, ["b"] = 0.0, ["a"] = 1.0 },
            }),
        };

        var result = Create().Sanitize(tree, new SanitizationRequest { ShouldDeduplicateStyles = true });

        Assert.Equal(0, result.StylesDeduplicated);
        Assert.Null(result.Node["$styles"]);
        Assert.IsType<JsonArray>(result.Node["fills"]);
    }

    [Fact]
    public void AllZeroCharacterOverrides_AreDropped()
    {
        var tree = new JsonObject
        {
            ["id"] = "1:1",
            ["type"] = "TEXT",
            ["characters"] = "Hello",
            ["characterStyleOverrides"] = new JsonArray(0, 0, 0, 0, 0, 0),
        };

        var result = Create().Sanitize(tree, new SanitizationRequest { Profile = SanitizationProfile.Aggressive });

        Assert.Null(result.Node["characterStyleOverrides"]);
        Assert.Equal("Hello", result.Node["characters"]!.GetValue<string>());
    }
}
