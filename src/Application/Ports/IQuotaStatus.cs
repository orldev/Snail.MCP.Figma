namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Published-source buckets currently closed by exhausted quota — for diagnostics.</summary>
public interface IQuotaStatus
{
    IReadOnlyDictionary<string, DateTimeOffset> ActiveBlocks { get; }
}
