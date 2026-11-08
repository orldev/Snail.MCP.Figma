using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Configuration;

/// <summary>Builds <see cref="ServerConfig"/>: defaults → the settings file → <c>SNAIL_MCP_FIGMA_</c> variables, as <see cref="ServerSettings"/> read them into the host's configuration.</summary>
public static class ServerConfigExtensions
{
    /// <summary>Binds the host's configuration and registers the built <see cref="ServerConfig"/> and the endpoint in DI, returning the config for host setup.</summary>
    /// <param name="builder">The host, its configuration the server's settings as the program read them.</param>
    /// <param name="endpoint">Where the server answers, as Snail.Sdk read it from the same settings: the one word on the transport, a <c>Port</c> included.</param>
    /// <remarks>The binding runs while the host is registered, before any host could refuse a value of the wrong type, so
    /// <see cref="ServerSettings.Bind(string, IConfiguration, Action{IConfiguration})"/> says it as a <see cref="StartRefusedException"/> naming
    /// the variable, where the binder crashed the process with a trace. The Bind calls stay written here, where the binding source generator of
    /// the native publication replaces them.</remarks>
    /// <exception cref="StartRefusedException">A setting holds a value its property's type cannot hold.</exception>
    public static ServerConfig AddServerConfiguration(this IHostApplicationBuilder builder, McpEndpoint endpoint)
    {
        var config = new ServerConfig();

        ServerSettings.Bind(ServerPaths.ShortName, builder.Configuration, settings =>
        {
            settings.GetSection(ServerConfig.SectionName).Bind(config);
            settings.Bind(config);
        });

        config.Transport = endpoint.Transport;

        Normalize(config);

        builder.Services.AddSingleton(endpoint);
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
        config.DataDirectory = ServerFolders.FullPath(config.DataDirectory, ServerPaths.DefaultDataDirectory);

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
}
