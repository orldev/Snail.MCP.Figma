namespace Snail.MCP.Figma.Application.Snapshots;

/// <summary>Last known version of a file; a separate record because it is checked on every read.</summary>
public sealed class DocumentVersionRecord
{
    public string? Id { get; set; }

    public required string FileKey { get; set; }

    public string? Version { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public string? Name { get; set; }

    public DateTimeOffset CheckedAt { get; set; }
}
