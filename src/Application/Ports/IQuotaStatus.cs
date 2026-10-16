namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Published-source buckets currently closed by exhausted quota — for diagnostics.</summary>
public interface IQuotaStatus
{
    /// <summary>Closed quota buckets with the moment each opens again; expired blocks are left out.</summary>
    IReadOnlyDictionary<string, DateTimeOffset> ActiveBlocks { get; }
}
