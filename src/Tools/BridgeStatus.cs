using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Snail.MCP.Figma.Tools;

/// <summary>One Figma file with the plugin running in it.</summary>
[SuppressMessage("Snail", "S3L0008", Justification = "open and target are fields of the output schema of figma_get_status and figma_list_open_files, which clients already read.")]
public sealed record ConnectedFile(
    [property: Description("Identity of the plugin connection.")] string ConnectionId,
    [property: Description("Key of the file, as in its URL.")] string? FileKey,
    string? FileName,
    string? CurrentPage,
    [property: Description("figma, figjam or slides.")] string? EditorType,
    string? PluginVersion,
    [property: Description("ISO 8601 instant the plugin connected.")] string ConnectedAt,
    [property: Description("Whether the connection is still open.")] bool Open,
    [property: Description("Whether this file is where the commands of this server go.")] bool Target,
    [property: Description("Names of the other agents bound to this file.")] IReadOnlyList<string> OthersHere);

/// <summary>What figma_get_status answers: the bridge, the target and the company in it.</summary>
[SuppressMessage("Snail", "S3L0008", Justification = "connected is a field of the output schema of figma_get_status, which clients already read.")]
public sealed record BridgeStatus(
    [property: Description("Whether at least one plugin session is open.")] bool Connected,
    [property: Description("Key of the target file; null when no file is bound.")] string? ActiveFileKey,
    [property: Description("Names of the other agents bound to the target file.")] IReadOnlyList<string> OthersHere,
    IReadOnlyList<ConnectedFile> Files);

/// <summary>What figma_list_open_files answers.</summary>
public sealed record OpenFiles(
    IReadOnlyList<ConnectedFile> Files,
    [property: Description("Key of the target file; null when no file is bound.")] string? ActiveFileKey);
