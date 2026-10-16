namespace Snail.MCP.Figma.Domain;

/// <summary>Node id in canonical colon form (<c>695:313</c>); links use dashes, the published source keys by colons.</summary>
public readonly record struct NodeId
{
    /// <summary>Node id from either form, stored in canonical colon form.</summary>
    public NodeId(string value) => Value = Normalize(value);

    /// <summary>The id in canonical colon form.</summary>
    public string Value { get; }

    /// <summary>Canonical string without creating a value — for places that need a plain string.</summary>
    public static string Normalize(string nodeId) => nodeId.Replace('-', ':');

    /// <summary>Node id from either form, normalized to colons.</summary>
    public static NodeId Parse(string value) => new(value);

    /// <summary>The id in canonical colon form.</summary>
    public override string ToString() => Value;
}
