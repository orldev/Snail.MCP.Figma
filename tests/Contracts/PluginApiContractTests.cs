using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Tests.Contracts;

/// <summary>Every figma.* method our JS calls must exist in the vendored Plugin API typings, and deprecated methods may be used only with an explicit reason.</summary>
public partial class PluginApiContractTests
{
    /// <summary>Deprecated methods we still call on purpose, with the reason.</summary>
    private static readonly Dictionary<string, string> DeprecatedAllowlist = new(StringComparer.Ordinal)
    {
        ["getSlideGrid"] = "fallback for Figma Desktop builds without getCanvasGrid",
        ["setSlideGrid"] = "fallback for Figma Desktop builds without setCanvasGrid",
    };

    [Fact]
    public void PluginApi_EveryCalledMethod_ExistsInTheTypings()
    {
        var declarations = Declarations();
        var missing = CalledMethods()
            .Where(call => !declarations.ContainsKey(Leaf(call)))
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0,
            $"methods our JS calls but @figma/plugin-typings does not declare: {string.Join(", ", missing)}. " +
            "Either a typo, or Figma removed the API — diff tests/Contracts/Figma/plugin-api.d.ts against the latest typings.");
    }

    [Fact]
    public void PluginApi_DeprecatedMethodCalled_HasAReasonOnRecord()
    {
        var declarations = Declarations();
        var offenders = CalledMethods()
            .Select(Leaf)
            .Distinct()
            .Where(leaf => declarations.TryGetValue(leaf, out var alive) && !alive && !DeprecatedAllowlist.ContainsKey(leaf))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"every declaration of these methods is @deprecated; replace them or add them to the allowlist with a reason: {string.Join(", ", offenders)}");
    }

    private static string Leaf(string call) => call[(call.LastIndexOf('.') + 1)..];

    /// <summary>All <c>figma.some.methodAsync(</c> call paths in the bundle, the injected scripts and the C# raw literals.</summary>
    private static IReadOnlyList<string> CalledMethods()
    {
        var root = FindRepositoryRoot();
        var sources = new List<string> { Path.Combine(root, "plugin", "code.js") };
        sources.AddRange(Directory.EnumerateFiles(Path.Combine(root, "src", "Adapters", "Editor", "Scripts"), "*.js"));
        sources.AddRange(Directory.EnumerateFiles(Path.Combine(root, "src", "Tools"), "*.cs"));

        return [.. sources
            .SelectMany(file => FigmaCallPattern.Matches(File.ReadAllText(file)).Select(match => match.Value.TrimEnd('(', ' ', '\t')))
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Member name → still alive; a name is dead only when every declaration of it carries @deprecated.</summary>
    private static Dictionary<string, bool> Declarations()
    {
        var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "tests", "Contracts", "Figma", "plugin-api.d.ts"));
        var members = new Dictionary<string, bool>(StringComparer.Ordinal);
        var pendingDeprecated = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("/**", StringComparison.Ordinal))
            {
                pendingDeprecated = false;
            }

            if (trimmed.Contains("@deprecated", StringComparison.Ordinal))
            {
                pendingDeprecated = true;

                continue;
            }

            if (trimmed.StartsWith("*", StringComparison.Ordinal) || trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (DeclarationPattern.Match(line) is { Success: true } match)
            {
                var name = match.Groups[1].Value;
                var alive = !pendingDeprecated;
                members[name] = members.TryGetValue(name, out var existing) ? existing || alive : alive;
            }

            pendingDeprecated = false;
        }

        Assert.True(members.Count > 500, "the vendored plugin-api.d.ts parsed suspiciously small — check tests/Contracts/Figma/plugin-api.d.ts");

        return members;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "Contracts", "Figma")))
            {
                return directory.FullName;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("tests/Contracts/Figma was not found above the test binaries");
    }

    [GeneratedRegex(@"\bfigma(\.[A-Za-z_][A-Za-z0-9_]*)+\s*\(")]
    private static partial Regex FigmaCallPattern { get; }

    [GeneratedRegex(@"^\s*(?:readonly\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*[(<:?]")]
    private static partial Regex DeclarationPattern { get; }
}
