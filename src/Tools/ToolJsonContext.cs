using System.Text.Json.Serialization;

namespace Snail.MCP.Figma.Tools;

/// <summary>The types the tools take and give, described by the source generator for the MCP SDK: their parameters and the answers that advertise an output schema.</summary>
/// <remarks>Generated at build time rather than read by reflection, so the server can be published with Native AOT; it reaches the SDK through <c>Snail.Sdk.Mcp.Hosting.ToolJson.Options</c>.</remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(Answer<BridgeStatus>))]
[JsonSerializable(typeof(Answer<OpenFiles>))]
internal sealed partial class ToolJsonContext : JsonSerializerContext;
