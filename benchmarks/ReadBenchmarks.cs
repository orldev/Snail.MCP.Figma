using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Benchmarks;

/// <summary>What every REST read pays before the model sees a node: the sanitizer over a whole file, as configured by default.</summary>
[MemoryDiagnoser]
public class ReadBenchmarks
{
    private readonly NodeSanitizer _sanitizer = new(new SanitizationOptions(), NullLogger<NodeSanitizer>.Instance);
    private JsonObject _file = null!;

    [GlobalSetup]
    public void Setup() => _file = Documents.Parsed(Documents.File());

    [Benchmark]
    public int SanitizeFile() => _sanitizer.Sanitize(_file, label: "benchmark").NodesEmitted;
}
