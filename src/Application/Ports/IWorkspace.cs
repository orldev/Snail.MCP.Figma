namespace Snail.MCP.Figma.Application.Ports;

/// <summary>One entry of a listed directory.</summary>
/// <param name="Path">Absolute path of the entry.</param>
/// <param name="Name">The entry's own name, without its directory.</param>
/// <param name="IsDirectory">Whether the entry is a directory rather than a file.</param>
/// <param name="IsLink">Whether the entry is a symbolic link or a junction, whatever it points at.</param>
/// <param name="Length">Size of a file in bytes; <c>-1</c> for a directory or a file whose size could not be read.</param>
public sealed record WorkspaceEntry(string Path, string Name, bool IsDirectory, bool IsLink, long Length);

/// <summary>The files of the user's machine the design-system extraction works on: the applications it reads and the package it writes.</summary>
/// <remarks>Reads answer <c>null</c> or an empty list for what cannot be read, so one unreadable file never fails a scan; writes throw, since a package that was not written is a failure the caller reports.</remarks>
public interface IWorkspace
{
    /// <summary>Whether a file exists at the path.</summary>
    bool HasFile(string path);

    /// <summary>Whether a directory exists at the path.</summary>
    bool HasDirectory(string path);

    /// <summary>The entries of a directory; empty when it does not exist or cannot be listed.</summary>
    IReadOnlyList<WorkspaceEntry> Entries(string directory);

    /// <summary>Whether the path itself is a link, or cannot be told apart from one.</summary>
    bool IsLink(string path);

    /// <summary>At most <paramref name="maxChars"/> and one more characters of a file as UTF-8 text, so the caller can tell a file that fits from one that does not; <c>null</c> when unreadable.</summary>
    string? ReadText(string path, int maxChars = int.MaxValue);

    /// <summary>Writes the text as the file's whole content, making its directory first.</summary>
    void WriteText(string path, string content);

    /// <summary>Writes the text beside the file and moves it over, so a reader meets the old content or the new and never half of either.</summary>
    void ReplaceText(string path, string content);

    /// <summary>Copies a file, making the destination's directory first.</summary>
    void Copy(string sourcePath, string destinationPath);
}
