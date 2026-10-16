namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>One file that reached analysis.</summary>
/// <param name="Path">Absolute path of the file.</param>
/// <param name="RelativePath">Path from the walk root, always forward-slashed.</param>
/// <param name="Extension">Lowercase extension with the dot.</param>
/// <param name="SizeBytes">Size of the file in bytes.</param>
public sealed record WalkedFile(string Path, string RelativePath, string Extension, long SizeBytes);

/// <summary>What the walk skipped — goes into target statistics.</summary>
public sealed class WalkSkipped
{
    /// <summary>How many files were left out for exceeding the size cap.</summary>
    public int TooLarge { get; set; }

    /// <summary>How many files were left out by the extension, include or exclude filters.</summary>
    public int Ignored { get; set; }

    /// <summary>Whether the walk stopped early because it reached the file limit.</summary>
    public bool IsOverMaxFiles { get; set; }
}

/// <summary>The files a walk collected and what it left out on the way.</summary>
/// <param name="Files">Files that passed every filter, in the order they were visited.</param>
/// <param name="Skipped">Counts of what the walk skipped.</param>
public sealed record WalkResult(IReadOnlyList<WalkedFile> Files, WalkSkipped Skipped);

/// <summary>Walk limits; without them a large app scan runs away.</summary>
public sealed record WalkOptions
{
    /// <summary>Extensions with the dot; empty means all.</summary>
    public IReadOnlyList<string>? Extensions { get; init; }

    /// <summary>Relative-path substrings; matching any keeps the file.</summary>
    public IReadOnlyList<string>? Include { get; init; }

    /// <summary>Relative-path substrings; matching any drops the file.</summary>
    public IReadOnlyList<string>? Exclude { get; init; }

    /// <summary>Number of files after which the walk stops collecting.</summary>
    public int MaxFiles { get; init; } = 5000;

    /// <summary>File size cap in kilobytes; anything larger is almost certainly generated.</summary>
    public int MaxFileSizeKb { get; init; } = 512;

    /// <summary>Number of directory levels below the root the walk descends at most.</summary>
    public int MaxDepth { get; init; } = 12;
}

/// <summary>Bounded file-system walk: symlinks are never followed and unreadable directories are skipped silently.</summary>
public static class CodeWalker
{
    /// <summary>Directories that never contain design-system sources.</summary>
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "dist", "build", "out", ".next", ".nuxt", ".svelte-kit",
        ".angular", ".turbo", ".cache", "coverage", ".storybook-static", "storybook-static",
        ".vercel", ".netlify", "vendor", "__snapshots__", ".extraction",
    };

    /// <summary>The files under <c>root</c> that pass the options' filters and limits, plus a count of what was skipped.</summary>
    public static WalkResult Walk(string root, WalkOptions? options = null)
    {
        var opts = options ?? new WalkOptions();
        var maxBytes = (long)opts.MaxFileSizeKb * 1024;
        var extensions = opts.Extensions is { Count: > 0 }
            ? new HashSet<string>(opts.Extensions.Select(e => e.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase)
            : null;

        var files = new List<WalkedFile>();
        var skipped = new WalkSkipped();

        Visit(root, root, 0, files, skipped, opts, extensions, maxBytes);

        return new WalkResult(files, skipped);
    }

    /// <summary>File text as UTF-8; <c>null</c> when unreadable, so one file cannot fail the scan.</summary>
    public static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>At most <paramref name="maxChars"/> and one more characters of a file, so the caller tells a file that fits from one that does not; <c>null</c> when unreadable.</summary>
    /// <remarks>A path a manifest names may be a device or a file of any size: read whole, <c>/dev/zero</c> never ends.</remarks>
    public static string? ReadText(string path, int maxChars)
    {
        try
        {
            using var reader = new StreamReader(path, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[maxChars + 1];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);

            return new string(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The absolute path of a file a manifest names under <paramref name="root"/>; <c>null</c> when it climbs out of the root or passes through a link.</summary>
    /// <remarks>A manifest is a file in the user's repository and may have come with a clone. The walk that wrote it never follows a link and never leaves the root, so a path that does either was not written by it — and would read the user's files into the model's context.</remarks>
    public static string? ResolveUnder(string root, string relativePath)
    {
        var resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(resolvedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var isInsideRoot = path.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (!isInsideRoot || PassesThroughLink(resolvedRoot, path))
        {
            return null;
        }

        return path;
    }

    private static bool PassesThroughLink(string root, string path)
    {
        for (var current = path; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
        {
            try
            {
                if (new FileInfo(current).LinkTarget is not null)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private static void Visit(
        string root,
        string directory,
        int depth,
        List<WalkedFile> files,
        WalkSkipped skipped,
        WalkOptions opts,
        HashSet<string>? extensions,
        long maxBytes)
    {
        if (files.Count >= opts.MaxFiles)
        {
            skipped.IsOverMaxFiles = true;

            return;
        }

        if (depth > opts.MaxDepth)
        {
            return;
        }

        FileSystemInfo[] entries;

        try
        {
            entries = new DirectoryInfo(directory).GetFileSystemInfos();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (files.Count >= opts.MaxFiles)
            {
                skipped.IsOverMaxFiles = true;

                return;
            }

            if (entry.LinkTarget is not null)
            {
                continue;
            }

            if (entry is DirectoryInfo subdirectory)
            {
                if (IgnoredDirectories.Contains(subdirectory.Name) || subdirectory.Name.StartsWith('.'))
                {
                    skipped.Ignored++;
                    continue;
                }

                Visit(root, subdirectory.FullName, depth + 1, files, skipped, opts, extensions, maxBytes);
                continue;
            }

            if (entry is not FileInfo file)
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
            var extension = file.Extension.ToLowerInvariant();

            if (extensions is not null && !extensions.Contains(extension))
            {
                continue;
            }

            if (opts.Exclude is { Count: > 0 } && opts.Exclude.Any(pattern => relative.Contains(pattern, StringComparison.Ordinal)))
            {
                skipped.Ignored++;
                continue;
            }

            if (opts.Include is { Count: > 0 } && !opts.Include.Any(pattern => relative.Contains(pattern, StringComparison.Ordinal)))
            {
                continue;
            }

            long size;

            try
            {
                size = file.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (size > maxBytes)
            {
                skipped.TooLarge++;
                continue;
            }

            files.Add(new WalkedFile(file.FullName, relative, extension, size));
        }
    }
}
