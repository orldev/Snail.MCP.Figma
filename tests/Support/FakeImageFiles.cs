using Snail.MCP.Figma.Application.Ports;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>Picture source answering with bytes the test prepared, so tool tests exercise the transfer without touching the disk.</summary>
public sealed class FakeImageFiles(string base64 = "iVBORw0KGgo=") : IImageFiles
{
    /// <summary>Paths the tools asked for, in order.</summary>
    public List<string> Requested { get; } = [];

    /// <summary>When set, every read fails with this message the way the real reader refuses a missing or oversized file.</summary>
    public string? Refusal { get; set; }

    public string ReadAsBase64(string path)
    {
        Requested.Add(path);

        return Refusal is null
            ? base64
            : throw new InvalidOperationException(Refusal);
    }
}
