namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>One file that reached analysis.</summary>
/// <param name="RelativePath">Path from the walk root, always forward-slashed.</param>
/// <param name="Extension">Lowercase extension with the dot.</param>
public sealed record WalkedFile(string Path, string RelativePath, string Extension, long SizeBytes);

/// <summary>What the walk skipped — goes into target statistics.</summary>
public sealed class WalkSkipped
{
    public int TooLarge { get; set; }

    public int Ignored { get; set; }

    public bool OverMaxFiles { get; set; }
}

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

    public int MaxFiles { get; init; } = 5000;

    /// <summary>File size cap in kilobytes; anything larger is almost certainly generated.</summary>
    public int MaxFileSizeKb { get; init; } = 512;

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
            skipped.OverMaxFiles = true;

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
                skipped.OverMaxFiles = true;

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
