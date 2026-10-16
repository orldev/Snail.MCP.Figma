using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Tests.Application;

/// <summary>The layering the guidelines ask for: Application is pure, and the file system is reached through <c>IWorkspace</c> from an adapter alone.</summary>
public partial class ApplicationBoundaryTests
{
    [GeneratedRegex(@"\bFile\.(Exists|Read|Write|Copy|Move|Delete|Open|Create|Append|GetLastWrite|SetUnix)|\bDirectory\.(Exists|Create|Get|Enumerate|Delete|Move)|new (FileInfo|DirectoryInfo|StreamReader|StreamWriter|FileStream)\(")]
    private static partial Regex FileSystemCall();

    /// <summary>A file-system call in Application cannot be faked and cannot be bounded in one place; it used to be how a manifest path read any file of the user.</summary>
    [Fact]
    public void ApplicationSources_Scanned_TouchNoFileSystem()
    {
        var application = Path.Combine(FindRepositoryRoot(), "src", "Application");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(application, "*.cs", SearchOption.AllDirectories))
        {
            var code = string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

            foreach (Match match in FileSystemCall().Matches(code))
            {
                offenders.Add($"{Path.GetRelativePath(application, file)}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0, $"Application reaches the file system directly: {string.Join("; ", offenders)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Application")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("the src folder was not found above the test binaries");
    }
}
