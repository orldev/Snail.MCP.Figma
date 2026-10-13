using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Configuration;

/// <summary>Builds <see cref="ServerConfig"/>: defaults → the settings file → <c>SNAIL_MCP_FIGMA_</c> variables, read through <see cref="ServerSettings"/>.</summary>
public static class ServerConfigExtensions
{
    /// <summary>Adds configuration sources and registers the built <see cref="ServerConfig"/> in DI, returning it for host setup.</summary>
    public static ServerConfig AddServerConfiguration(this IHostApplicationBuilder builder)
    {
        builder.Configuration.AddServerSettings(ServerPaths.ShortName);

        var config = new ServerConfig();
        builder.Configuration.GetSection(ServerConfig.SectionName).Bind(config);
        builder.Configuration.Bind(config);

        Normalize(config);

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(config.Bridge);
        builder.Services.AddSingleton(config.Cache);
        builder.Services.AddSingleton(config.Sanitization);
        builder.Services.AddSingleton(config.Screenshots);

        return config;
    }

    /// <summary>Expands <c>~</c> and fixes obviously invalid ranges and timeouts.</summary>
    internal static void Normalize(ServerConfig config)
    {
        config.AccessToken = string.IsNullOrWhiteSpace(config.AccessToken) ? null : config.AccessToken.Trim();
        config.DataDirectory = ExpandHome(config.DataDirectory);

        if (config.Bridge.PortRangeEnd < config.Bridge.PortRangeStart)
        {
            config.Bridge.PortRangeEnd = config.Bridge.PortRangeStart;
        }

        if (config.Bridge.RequestTimeoutSeconds <= 0)
        {
            config.Bridge.RequestTimeoutSeconds = 30;
        }

        if (!config.Bridge.Path.StartsWith('/'))
        {
            config.Bridge.Path = $"/{config.Bridge.Path}";
        }

        if (config.Cache.TtlHours <= 0)
        {
            config.Cache.TtlHours = 24;
        }

        if (config.Sanitization.DefaultDepth <= 0)
        {
            config.Sanitization.DefaultDepth = 3;
        }

        if (config.Sanitization.DefaultMaxNodes <= 0)
        {
            config.Sanitization.DefaultMaxNodes = 500;
        }

        if (config.Screenshots.RetainCount <= 0)
        {
            config.Screenshots.RetainCount = 30;
        }
    }

    private static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ServerPaths.DefaultDataDirectory;
        }

        path = path.Trim();

        if (path == "~")
        {
            return ServerPaths.Home;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal))
        {
            return Path.Combine(ServerPaths.Home, path[2..]);
        }

        return Path.GetFullPath(path);
    }
}
