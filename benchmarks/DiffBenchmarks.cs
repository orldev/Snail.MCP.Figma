using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Snail.MCP.Figma.Application.Diff;

namespace Snail.MCP.Figma.Benchmarks;

/// <summary>figma_diff_versions on a component set: children, property definitions and variable bindings of two versions.</summary>
[MemoryDiagnoser]
public class DiffBenchmarks
{
    private JsonObject _before = null!;
    private JsonObject _after = null!;

    [GlobalSetup]
    public void Setup()
    {
        _before = Documents.Parsed(Documents.ComponentSet(revision: 0));
        _after = Documents.Parsed(Documents.ComponentSet(revision: 1));
    }

    [Benchmark]
    public int DiffComponentSet() => DiffEngine.DiffNode(_before, _after, DiffModes.Detailed)["change_count"]!.GetValue<int>();
}
