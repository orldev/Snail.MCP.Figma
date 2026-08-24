using Microsoft.Extensions.Configuration;

namespace Snail.MCP.Figma.Configuration;

/// <summary>Maps <c>SNAIL_MCP_FIGMA_SECTION__FIELD_NAME</c> variables onto configuration keys <c>Section:FieldName</c>.</summary>
public static class EnvironmentSettings
{
    public const string Prefix = "SNAIL_MCP_FIGMA_";

    public const string ConfigPathVariable = $"{Prefix}CONFIG";

    public static IConfigurationBuilder AddEnvironment(this IConfigurationBuilder builder) =>
        builder.AddInMemoryCollection(Read(Environment.GetEnvironmentVariables()));

    internal static IEnumerable<KeyValuePair<string, string?>> Read(System.Collections.IDictionary variables)
    {
        foreach (System.Collections.DictionaryEntry entry in variables)
        {
            if (entry.Key is not string name || !name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Value is not string value || string.IsNullOrWhiteSpace(value)) continue;

            yield return new KeyValuePair<string, string?>(ToConfigurationKey(name[Prefix.Length..]), value.Trim());
        }
    }

    internal static string ToConfigurationKey(string variable) =>
        string.Join(':', variable.Split("__").Select(segment => segment.Replace("_", "")));
}
