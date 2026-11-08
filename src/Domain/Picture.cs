namespace Snail.MCP.Figma.Domain;

/// <summary>A picture ready to travel inside a tool result: its bytes as they are encoded, a PNG or a JPEG, and what they are.</summary>
/// <remarks>The tool result takes the bytes themselves (Snail.Sdk's <c>WithImage</c>), which the protocol block encodes as base64 on the wire.</remarks>
public readonly record struct Picture(byte[] Bytes, string MimeType);
