using System.Text;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Snail.MCP.Figma.Adapters.Editor;

namespace Snail.MCP.Figma.Benchmarks;

/// <summary>What the bridge pays for one plugin message before any handler sees it: a screenshot reply, the largest message the plugin sends.</summary>
[MemoryDiagnoser]
public class BridgeBenchmarks
{
    private MemoryStream _screenshotReply = null!;

    [GlobalSetup]
    public void Setup()
    {
        var picture = Convert.ToBase64String(new byte[4 * 1024 * 1024]);
        var reply = $$$$"""{"id":"snail_1_1792170000000","result":{"image":{"base64":"{{{{picture}}}}","format":"PNG","scale":2}}}""";

        _screenshotReply = new MemoryStream();
        _screenshotReply.Write(Encoding.UTF8.GetBytes(reply));
    }

    [Benchmark]
    public int ParseScreenshotReply() => EditorWireProtocol.Parse(_screenshotReply)?["result"] is JsonObject result ? result.Count : 0;
}
