namespace Snail.MCP.Figma.Domain;

/// <summary>A picture ready to travel inside a tool result.</summary>
/// <remarks>The bytes stay base64 text rather than becoming a byte array, because that is what the protocol block carries: it takes the base64 TEXT as UTF-8 bytes, and decoding here would put raw PNG on the wire for the client to reject as invalid base64.</remarks>
public readonly record struct Picture(string Base64, string MimeType);
