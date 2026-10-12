using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Tests.Contracts;

/// <summary>Pins the test double of the variables payload to the bundle: the day code.js changes what it sends, the sample the other tests trust stops being the truth, and this is where that shows.</summary>
public class VariablesPayloadContractTests
{
    private static readonly string Bundle = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "plugin", "code.js"));

    [Theory]
    [InlineData("serializeVariable")]
    [InlineData("serializeCollection")]
    public void TheSampleCarriesExactlyTheFieldsTheBundleSerializes(string serializer)
    {
        var body = Regex.Match(Bundle, $@"function {serializer}\(\w+\) \{{\s*return \{{(?<fields>.*?)\}};", RegexOptions.Singleline);

        Assert.True(body.Success, $"{serializer} was not found in plugin/code.js");

        var serialized = Regex.Matches(body.Groups["fields"].Value, @"^\s*(\w+):", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToList();

        var expected = serializer == "serializeVariable" ? PluginVariables.VariableFields : PluginVariables.CollectionFields;

        Assert.Equal(expected, serialized);
        Assert.Equal(expected.Order(), SampleItem(serializer).Select(pair => pair.Key).Order());
    }

    /// <summary>Both places that build the payload, the startup push and REFRESH_VARIABLES, send arrays; the readers key them by id.</summary>
    [Fact]
    public void BothSectionsLeaveTheBundleAsArrays()
    {
        Assert.Equal(2, Regex.Matches(Bundle, @"\bvariables:\s*variables\.map\(").Count);
        Assert.Equal(2, Regex.Matches(Bundle, @"\bvariableCollections:\s*collections\.map\(").Count);
    }

    private static JsonObject SampleItem(string serializer) =>
        PluginVariables.Payload()[serializer == "serializeVariable" ? "variables" : "variableCollections"]![0]!.AsObject();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "plugin")))
            {
                return directory.FullName;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("the plugin folder was not found above the test binaries");
    }
}
