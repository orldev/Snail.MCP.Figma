namespace Snail.MCP.Figma.Domain;

/// <summary>The plugin is not connected or the connection was lost.</summary>
public sealed class EditorUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>The plugin accepted the command but could not execute it.</summary>
public sealed class EditorCommandException(string message) : Exception(message);

/// <summary>Several files are connected and the agent has not said which one it works in.</summary>
/// <remarks>Picking one would send a write into somebody else's file; the message names the choices so the agent can answer.</remarks>
public sealed class EditorTargetAmbiguousException(string message) : Exception(message);
