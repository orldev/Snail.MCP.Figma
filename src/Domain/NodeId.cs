namespace Snail.MCP.Figma.Domain;

/// <summary>Node id in canonical colon form (<c>695:313</c>); links use dashes, the published source keys by colons.</summary>
public readonly record struct NodeId
{
    public NodeId(string value) => Value = Normalize(value);

    public string Value { get; }

    /// <summary>Canonical string without creating a value — for places that need a plain string.</summary>
    public static string Normalize(string nodeId) => nodeId.Replace('-', ':');

    public static NodeId Parse(string value) => new(value);

    public override string ToString() => Value;
}
