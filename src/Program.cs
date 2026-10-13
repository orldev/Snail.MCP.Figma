using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Extensions;
using Snail.Sdk.Mcp.Hosting;

ServerFolders.MoveFromSnailNames("figma");

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });
builder.WebHost.UseKestrelCore();
builder.Services.AddRoutingCore();

var config = builder.AddServerConfiguration();

if (config.IsShared)
{
    await builder
        .ConfigureSharedMcpServer(config)
        .Build()
        .UseBridge()
        .UseSharedMcp()
        .RunUntilStoppedAsync();

    return;
}

await builder
    .ConfigureMcpServer(config)
    .Build()
    .UseBridge()
    .RunUntilStoppedAsync();
