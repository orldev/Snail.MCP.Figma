namespace Snail.MCP.Figma.Application.Sanitization;

/// <summary>Sanitization result with savings metrics.</summary>
/// <param name="Node">The cleaned tree.</param>
/// <param name="BytesIn">Size of the source tree as compact JSON.</param>
/// <param name="BytesOut">Size of the cleaned tree as compact JSON.</param>
/// <param name="NodesEmitted">Nodes included in the response.</param>
/// <param name="NodesOmitted">Nodes dropped by depth and count limits.</param>
/// <param name="StylesDeduplicated">Repeated styles moved into the dictionary.</param>
public sealed record SanitizationResult(
    JsonNode Node,
    long BytesIn,
    long BytesOut,
    int NodesEmitted,
    int NodesOmitted,
    int StylesDeduplicated)
{
    /// <summary>Fraction removed, 0..1 — the promised token savings.</summary>
    public double Ratio => BytesIn <= 0 ? 0 : 1d - ((double)BytesOut / BytesIn);

    /// <summary>Metric line for stderr.</summary>
    public string ToMetricString() =>
        $"bytes_in={BytesIn} bytes_out={BytesOut} ratio={Ratio:P1} nodes={NodesEmitted}{(NodesOmitted > 0 ? $" omitted={NodesOmitted}" : string.Empty)}{(StylesDeduplicated > 0 ? $" styles_deduped={StylesDeduplicated}" : string.Empty)}";
}
