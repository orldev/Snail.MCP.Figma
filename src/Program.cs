using Microsoft.AspNetCore.Builder;
using Snail.MCP.Figma.Extensions;

await WebApplication.CreateBuilder(args)
    .ConfigureMcpServer()
    .Build()
    .UseBridge()
    .RunAsync();
