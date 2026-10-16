namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Where the editor extension lives: next to the assembly and in the directory the editor imports it from.</summary>
public interface IEditorExtensionInstaller
{
    /// <summary>Extension directory next to the assembly; <c>null</c> when the build has none.</summary>
    string? BundledDirectory { get; }

    /// <summary>Directory the extension is installed into for the editor to import it from.</summary>
    string TargetDirectory { get; }

    /// <summary>Whether the target directory holds an installed extension manifest.</summary>
    bool IsInstalled { get; }
}
