using Microsoft.Extensions.Configuration;

namespace Snail.MCP.Figma.Configuration;

/// <summary>Maps <c>SNAIL_MCP_FIGMA_SECTION__FIELD_NAME</c> variables onto configuration keys <c>Section:FieldName</c>.</summary>
/// <remarks>The reading is Snail.Sdk.Mcp.Hosting's, the same in every Snail server; only the prefix is this server's.</remarks>
public static class EnvironmentSettings
{
    public const string Prefix = "SNAIL_MCP_FIGMA_";

    public const string ConfigPathVariable = $"{Prefix}CONFIG";

    public static IConfigurationBuilder AddEnvironment(this IConfigurationBuilder builder) =>
        builder.AddInMemoryCollection(Read(Environment.GetEnvironmentVariables()));

    internal static IEnumerable<KeyValuePair<string, string?>> Read(System.Collections.IDictionary variables) =>
        Snail.Sdk.Mcp.Hosting.EnvironmentSettings.Read(Prefix, variables);

    internal static string ToConfigurationKey(string variable) =>
        Snail.Sdk.Mcp.Hosting.EnvironmentSettings.ToConfigurationKey(variable);
}
