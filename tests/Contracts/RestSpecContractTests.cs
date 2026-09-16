using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Tests.Contracts;

/// <summary>Every REST route the gateway calls must exist in the vendored Figma OpenAPI spec; a red run after refreshing tests/Contracts/Figma means Figma changed something we use.</summary>
public class RestSpecContractTests
{
    /// <summary>Operations the gateway is expected to construct; bump when an endpoint is added or removed.</summary>
    private const int ExpectedOperationCount = 19;

    [Fact]
    public void EveryGatewayRouteExistsInTheSpec()
    {
        var spec = SpecOperations();
        var used = GatewayOperations();

        var missing = used.Where(operation => !spec.Contains(operation)).ToList();

        Assert.True(missing.Count == 0,
            $"routes the gateway calls but the vendored spec does not know: {string.Join(", ", missing.Select(m => $"{m.Verb} {m.Path}"))}. " +
            "Either the route is wrong, or Figma removed it — diff tests/Contracts/Figma/openapi.yaml against the latest @figma/rest-api-spec.");
    }

    [Fact]
    public void TheExtractorSeesEveryGatewayOperation()
    {
        var used = GatewayOperations();

        Assert.True(used.Count == ExpectedOperationCount,
            $"extracted {used.Count} operations from RestDocumentGateway, expected {ExpectedOperationCount}. " +
            $"Added or removed an endpoint? Update ExpectedOperationCount. Extractor missed a route shape? Fix the extractor: {string.Join(", ", used.Select(m => $"{m.Verb} {m.Path}").OrderBy(x => x))}");
    }

    private static HashSet<(string Verb, string Path)> GatewayOperations()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Adapters", "Rest", "RestDocumentGateway.cs"));
        var operations = new HashSet<(string, string)>();

        foreach (Match call in Regex.Matches(source, @"(Get|Post|Delete)Async\(\s*\$?""(/[^""]*)""|endpoint = \$""(/[^""]*)"""))
        {
            var verb = call.Groups[1].Success
                ? call.Groups[1].Value.ToUpperInvariant() switch { "GET" => "GET", "POST" => "POST", _ => "DELETE" }
                : "GET";
            var template = call.Groups[2].Success ? call.Groups[2].Value : call.Groups[3].Value;

            operations.Add((verb, Normalize(template)));
        }

        return operations;
    }

    /// <summary>Interpolations become <c>{p}</c>; query strings and non-path-segment interpolations are dropped.</summary>
    internal static string Normalize(string template)
    {
        var builder = new System.Text.StringBuilder();

        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                builder.Append(template[i]);

                continue;
            }

            var depth = 1;
            var start = i;

            while (depth > 0 && ++i < template.Length)
            {
                depth += template[i] switch { '{' => 1, '}' => -1, _ => 0 };
            }

            if (IsPathSegment(template, start))
            {
                builder.Append("{p}");
            }
        }

        var path = builder.ToString();
        var query = path.IndexOf('?');

        return $"/v1{(query >= 0 ? path[..query] : path).TrimEnd('/')}";
    }

    /// <summary>A placeholder is a path segment only right after '/'; anything else is an inline query or suffix.</summary>
    private static bool IsPathSegment(string template, int start) => start > 0 && template[start - 1] == '/';

    private static HashSet<(string Verb, string Path)> SpecOperations()
    {
        var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "tests", "Contracts", "Figma", "openapi.yaml"));
        var operations = new HashSet<(string, string)>();
        var inPaths = false;
        string? currentPath = null;

        foreach (var line in lines)
        {
            if (line == "paths:")
            {
                inPaths = true;

                continue;
            }

            if (!inPaths)
            {
                continue;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            if (Regex.Match(line, @"^  (/\S+):\s*$") is { Success: true } path)
            {
                currentPath = Regex.Replace(path.Groups[1].Value, @"\{[^}]*\}", "{p}");

                continue;
            }

            if (currentPath is not null && Regex.Match(line, @"^    (get|post|put|delete|patch):\s*$") is { Success: true } verb)
            {
                operations.Add((verb.Groups[1].Value.ToUpperInvariant(), currentPath));
            }
        }

        Assert.True(operations.Count > 30, "the vendored openapi.yaml parsed suspiciously small — check tests/Contracts/Figma/openapi.yaml");

        return operations;
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
}
