using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Extensions;

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });
builder.WebHost.UseKestrelCore();
builder.Services.AddRoutingCore();

await builder
    .ConfigureMcpServer()
    .Build()
    .UseBridge()
    .RunAsync();
