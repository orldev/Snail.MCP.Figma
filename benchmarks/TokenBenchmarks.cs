using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Formatters;

namespace Snail.MCP.Figma.Benchmarks;

/// <summary>figma_export_tokens: two hundred variables into the token model, then into CSS variables and DTCG JSON.</summary>
[MemoryDiagnoser]
public class TokenBenchmarks
{
    private static readonly ITokenFormatter Css = TokenFormatters.Find("css-vars")!;
    private static readonly ITokenFormatter Dtcg = TokenFormatters.Find("dtcg")!;
    private JsonObject _variables = null!;

    [GlobalSetup]
    public void Setup() => _variables = Documents.Parsed(Documents.Variables());

    [Benchmark]
    public int ExportTokens()
    {
        var document = FigmaVariableConverter.Convert(_variables);
        var css = Css.Write(document, new FormatOptions());
        var dtcg = Dtcg.Write(document, new FormatOptions());

        return css.Files.Sum(file => file.Content.Length) + dtcg.Files.Sum(file => file.Content.Length);
    }
}
