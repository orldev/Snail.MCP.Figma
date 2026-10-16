using System.Text;

namespace Snail.MCP.Figma.Adapters.Workspace;

/// <summary>The machine's own file system as the extraction's workspace.</summary>
public sealed class LocalWorkspace : IWorkspace
{
    /// <summary>How much of a file one read takes before looking at the limit again.</summary>
    private const int ReadBlockChars = 64 * 1024;

    /// <inheritdoc/>
    public bool HasFile(string path) => File.Exists(path);

    /// <inheritdoc/>
    public bool HasDirectory(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public IReadOnlyList<WorkspaceEntry> Entries(string directory)
    {
        FileSystemInfo[] found;

        try
        {
            found = new DirectoryInfo(directory).GetFileSystemInfos();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        return [.. found.Select(Describe)];
    }

    /// <inheritdoc/>
    public bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Read in blocks up to the limit rather than whole: a path a manifest names may be a device, and <c>/dev/zero</c> never ends.</remarks>
    public string? ReadText(string path, int maxChars = int.MaxValue)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var wanted = maxChars == int.MaxValue ? int.MaxValue : maxChars + 1;
            var text = new StringBuilder();
            var block = new char[ReadBlockChars];

            while (text.Length < wanted)
            {
                var read = reader.ReadBlock(block, 0, (int)Math.Min(block.Length, wanted - text.Length));

                if (read == 0)
                {
                    break;
                }

                text.Append(block, 0, read);
            }

            return text.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void WriteText(string path, string content)
    {
        MakeDirectoryOf(path);
        File.WriteAllText(path, content);
    }

    /// <inheritdoc/>
    /// <remarks>The name beside it is unique because two server processes may write the same file at once.</remarks>
    public void ReplaceText(string path, string content)
    {
        MakeDirectoryOf(path);
        var written = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(written, content);
        File.Move(written, path, overwrite: true);
    }

    /// <inheritdoc/>
    public void Copy(string sourcePath, string destinationPath)
    {
        MakeDirectoryOf(destinationPath);
        File.Copy(sourcePath, destinationPath);
    }

    private static void MakeDirectoryOf(string path)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static WorkspaceEntry Describe(FileSystemInfo entry)
    {
        var isDirectory = entry is DirectoryInfo;
        var length = entry is FileInfo file ? LengthOf(file) : -1;

        return new WorkspaceEntry(entry.FullName, entry.Name, isDirectory, entry.LinkTarget is not null, length);
    }

    private static long LengthOf(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }
}
