namespace Snail.MCP.Figma.Application.Snapshots;

/// <summary>Last known version of a file; a separate record because it is checked on every read.</summary>
public sealed class DocumentVersionRecord
{
    /// <summary>Record identifier; the SQLite ledger neither sets nor reads it and keys records by file.</summary>
    public string? Id { get; set; }

    /// <summary>Key of the file the version belongs to.</summary>
    public required string FileKey { get; set; }

    /// <summary>Version id from the last read envelope; <c>null</c> when the source did not report one.</summary>
    public string? Version { get; set; }

    /// <summary>When the file was last modified, as the source reported it.</summary>
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>File name, as the source reported it.</summary>
    public string? Name { get; set; }

    /// <summary>When this version was last recorded.</summary>
    public DateTimeOffset CheckedAt { get; set; }
}
