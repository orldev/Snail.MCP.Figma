namespace Snail.MCP.Figma.Domain;

/// <summary>The plugin is not connected or the connection was lost.</summary>
public sealed class EditorUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>The plugin accepted the command but could not execute it.</summary>
public sealed class EditorCommandException(string message) : Exception(message);
