namespace Snail.MCP.Figma.Domain;

/// <summary>What the human said when they interrupted a running build.</summary>
/// <remarks>The button interrupts without words and the panel's field interrupts with them, so the remark and the selection are optional: what always matters is that a person stepped in.</remarks>
/// <param name="Remark">What the human wrote, or <c>null</c> when they only pressed Stop.</param>
/// <param name="Nodes">What was selected as they wrote it, which is how a remark points at a part of a big frame.</param>
/// <param name="At">When the human stepped in.</param>
public sealed record BuildInterruption(string? Remark, IReadOnlyList<JsonNode> Nodes, DateTimeOffset At);
