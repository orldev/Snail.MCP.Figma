using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

public sealed class CaptureStoreTests : IDisposable
{
    private readonly string _dataDirectory =
        Path.Combine(TestFolder.Create(), "data");

    private CaptureStore CreateStore(int retainCount = 30) =>
        new(
            new ServerConfig { DataDirectory = _dataDirectory, Screenshots = new ScreenshotOptions { RetainCount = retainCount } },
            NullLogger<CaptureStore>.Instance);

    private static JsonObject Image(string base64, string format = "PNG") => new()
    {
        ["base64"] = base64,
        ["format"] = format,
        ["byteLength"] = 3,
        ["node"] = new JsonObject { ["id"] = "1:1", ["name"] = "Frame", ["type"] = "FRAME" },
    };

    [Fact]
    public void CaptureStore_ImageSaved_WritesFileAndReplacesBase64WithPath()
    {
        var store = CreateStore();
        var image = Image(Convert.ToBase64String([1, 2, 3]));

        var result = store.SaveAndReplace(image);

        Assert.False(result.ContainsKey("base64"));
        var path = result["path"]!.GetValue<string>();
        Assert.True(File.Exists(path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        Assert.Equal("1:1", result["node"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void CaptureStore_FormatGiven_UsesItAsFileExtension()
    {
        var store = CreateStore();
        var image = Image(Convert.ToBase64String([1]), format: "JPG");

        var result = store.SaveAndReplace(image);

        Assert.EndsWith(".jpg", result["path"]!.GetValue<string>());
    }

    [Fact]
    public void SaveAndReplace_MissingBase64_Throws()
    {
        var store = CreateStore();
        var image = new JsonObject { ["format"] = "PNG" };

        Assert.Throws<InvalidOperationException>(() => store.SaveAndReplace(image));
    }

    [Fact]
    public void CaptureStore_MoreImagesThanRetainCount_KeepsOnlyRetainCount()
    {
        var store = CreateStore(retainCount: 2);

        for (var i = 0; i < 5; i++)
        {
            store.SaveAndReplace(Image(Convert.ToBase64String([(byte)i])));
        }

        var remaining = Directory.EnumerateFiles(Path.Combine(_dataDirectory, "screenshots")).ToList();
        Assert.Equal(2, remaining.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }
}
