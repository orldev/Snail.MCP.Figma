using System.ComponentModel;

namespace Snail.MCP.Figma.Tools;

/// <summary>The success envelope of a tool that advertises an output schema.</summary>
/// <remarks>A host that turns tools into code stubs takes the return type from this schema. A failure is the same envelope with <c>ok</c> false, <c>error</c> and <c>hint</c>, sent as text only, so it never has to conform.</remarks>
public sealed record Answer<T>(
    [property: Description("true on success; a failure comes as text with ok false, error and hint.")] bool Ok,
    T Data);

/// <summary>One Figma file with the plugin running in it.</summary>
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
public sealed record BridgeStatus(
    [property: Description("Whether at least one plugin session is open.")] bool Connected,
    [property: Description("Key of the target file; null when no file is bound.")] string? ActiveFileKey,
    [property: Description("Names of the other agents bound to the target file.")] IReadOnlyList<string> OthersHere,
    IReadOnlyList<ConnectedFile> Files);

/// <summary>What figma_list_open_files answers.</summary>
public sealed record OpenFiles(
    IReadOnlyList<ConnectedFile> Files,
    [property: Description("Key of the target file; null when no file is bound.")] string? ActiveFileKey);
