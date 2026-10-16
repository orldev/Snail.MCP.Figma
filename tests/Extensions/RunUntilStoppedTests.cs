using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.Figma.Extensions;

namespace Snail.MCP.Figma.Tests.Extensions;

/// <summary>Over stdio the MCP transport starts before Kestrel; a client that closes its input at once asks the host to stop while Kestrel is still binding.</summary>
public class RunUntilStoppedTests
{
    private static WebApplication StoppedWhileStarting()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Services.AddRoutingCore();
        builder.Services.AddHostedService<StopsAsItStarts>();

        return builder.Build();
    }

    [Fact]
    public async Task RunUntilStopped_StopAskedWhileStarting_EndsWithoutAFailure()
    {
        await using var app = StoppedWhileStarting();

        await app.RunUntilStoppedAsync().WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>What the guard is for: the host itself treats such a stop as a failed start.</summary>
    [Fact]
    public async Task RunAsync_SameStopWithoutTheGuard_FailsTheStart()
    {
        await using var app = StoppedWhileStarting();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.RunAsync().WaitAsync(TimeSpan.FromSeconds(30)));
    }

    /// <summary>The stdio transport as a client with empty input meets it: done before the server behind it has started.</summary>
    private sealed class StopsAsItStarts(IHostApplicationLifetime lifetime) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            lifetime.StopApplication();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
