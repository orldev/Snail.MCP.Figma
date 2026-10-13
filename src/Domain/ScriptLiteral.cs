using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.MCP.Figma.Domain;

/// <summary>A value written as a JavaScript literal, to be put into a script the plugin runs or a file the tools write.</summary>
/// <remarks>JSON is valid JavaScript for strings and arrays of them, and the serializer escapes what a hand-built quote would not. The JSON is generated at build time, so the server can be published with Native AOT.</remarks>
public static class ScriptLiteral
{
    /// <summary>The string quoted and escaped; <c>null</c> becomes the literal <c>null</c>.</summary>
    public static string Of(string? value) => JsonSerializer.Serialize(value, ScriptLiteralJson.Default.String);

    /// <summary>The strings as an array literal.</summary>
    public static string Of(string[] values) => JsonSerializer.Serialize(values, ScriptLiteralJson.Default.StringArray);
}

/// <summary>The types <see cref="ScriptLiteral"/> writes, described by the source generator.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class ScriptLiteralJson : JsonSerializerContext;
