using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>The secret a panel proves it is this machine's installed plugin with: written once into the data folder, stamped into the installed <c>ui.html</c>, sent back in <c>FILE_INFO</c>.</summary>
/// <remarks>
/// Figma runs a plugin's window with Origin <c>null</c>, so the bridge cannot refuse that origin — and any web page can take it in a
/// sandboxed iframe, connect, name a file it read from <c>/health</c>, take that file's commands and hand the agents answers and
/// "remarks" of its own. A page cannot read a file on disk; the installed plugin can, because the installer wrote the secret into it.
/// Every server of the machine shares the data folder, so all of them know the same secret, and one panel serves them all.
/// </remarks>
public sealed class BridgeToken(string value)
{
    /// <summary>What the installer replaces in <c>ui.html</c>; a panel still carrying it was not installed by a server.</summary>
    public const string Placeholder = "__SNAIL_BRIDGE_TOKEN__";

    /// <summary>The name of the file in the data folder.</summary>
    public const string FileName = "bridge.token";

    /// <summary>The secret itself.</summary>
    public string Value { get; } = value;

    /// <summary>Whether the panel sent this machine's secret, compared in constant time.</summary>
    public bool IsMatchedBy(string? offered) => ServerSecret.Matches(Value, offered);

    /// <summary>The machine's secret from the data folder, made the first time a server starts there: 32 random bytes as 64 hex digits.</summary>
    /// <remarks>Snail.Sdk's <see cref="ServerSecret.ReadOrCreateHex(string)"/>: readable by its owner alone, written whole under a lock, so every server starting at once agrees on one, and never replaced — a file the plugin was stamped with stays valid.</remarks>
    /// <exception cref="ServerSecretRefusedException">The file holds something other than 64 hex digits; it is left as it is.</exception>
    public static BridgeToken LoadOrCreate(string dataDirectory) =>
        new(ServerSecret.ReadOrCreateHex(Path.Combine(dataDirectory, FileName)));
}
