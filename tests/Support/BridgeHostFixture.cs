using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Adapters.Persistence.LiteDb;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Extensions;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>Hosts a real WebSocket hub on a free port, wired through the production <c>UseBridge()</c> — the same route, WebSocket upgrade and /health the real server maps; the bridge is the only write path and half of its contract lives in the socket.</summary>
public sealed class BridgeHostFixture : IAsyncLifetime
{
    /// <summary>Outside 9223-9232 — the range the real Figma plugin scans; inside it the plugin would hijack the test hub.</summary>
    private const int SafePortRangeStart = 19223;

    private const int SafePortRangeEnd = 19262;

    private WebApplication? _app;
    private string _bridgePath = "/";

    public int Port { get; private set; }

    public LiveEditorGateway Gateway { get; private set; } = null!;

    public OpenDocumentRegistry Registry { get; private set; } = null!;

    public LiteDbNodeSnapshotStore Cache { get; private set; } = null!;

    public EditorSocketHub Hub { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var config = new ServerConfig
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "snail-bridge-tests", Guid.NewGuid().ToString("N")),
            Cache = { Enabled = false },
            Bridge = { RequestTimeoutSeconds = 5, PortRangeStart = SafePortRangeStart, PortRangeEnd = SafePortRangeEnd },
        };

        Port = PortDiscovery.FindAvailablePort(config.Bridge)
               ?? throw new InvalidOperationException("no free port in the plugin range");

        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(Port));

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(config.Bridge);
        builder.Services.AddSingleton(config.Cache);
        builder.Services.AddSingleton<ActiveDocumentTracker>();
        builder.Services.AddSingleton<ILiteDatabaseProvider, DisabledLiteDatabaseProvider>();
        builder.Services.AddSingleton<LiteDbNodeSnapshotStore>();
        builder.Services.AddSingleton<INodeSnapshotStore>(provider => provider.GetRequiredService<LiteDbNodeSnapshotStore>());
        builder.Services.AddSingleton<LiteDbRenderSnapshotStore>();
        builder.Services.AddSingleton<IRenderSnapshotStore>(provider => provider.GetRequiredService<LiteDbRenderSnapshotStore>());
        var source = new FakePublishedSource(new JsonObject());
        builder.Services.AddSingleton<IPublishedDocumentSource>(source);
        builder.Services.AddSingleton<IRenderSource>(source);
        builder.Services.AddSingleton<LiteDbDocumentVersionLedger>();
        builder.Services.AddSingleton<IDocumentVersionLedger>(provider => provider.GetRequiredService<LiteDbDocumentVersionLedger>());
        builder.Services.AddSingleton<SnapshotInvalidator>();
        builder.Services.AddSingleton<OpenDocumentRegistry>();
        builder.Services.AddSingleton<EditorCommandRouter>();
        builder.Services.AddSingleton<EditorSocketHub>();
        builder.Services.AddSingleton<LiveEditorGateway>();
        builder.Services.AddSingleton(new EditorLinkEndpoint(Port, config.Bridge.Path));

        _app = builder.Build();

        _app.UseBridge();
        _bridgePath = config.Bridge.Path;

        await _app.StartAsync();

        Gateway = _app.Services.GetRequiredService<LiveEditorGateway>();
        Hub = _app.Services.GetRequiredService<EditorSocketHub>();
        Registry = _app.Services.GetRequiredService<OpenDocumentRegistry>();
        Cache = _app.Services.GetRequiredService<LiteDbNodeSnapshotStore>();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>Connects a fake plugin and waits until the hub recognizes it.</summary>
    public async Task<FakeEditorExtension> ConnectPluginAsync(string fileKey, string fileName = "Test file")
    {
        var plugin = new FakeEditorExtension();
        await plugin.ConnectAsync(Port, fileKey, fileName, _bridgePath);

        await WaitUntilAsync(() => Registry.Resolve(fileKey) is not null,
            $"hub did not register file {fileKey}");

        return plugin;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string message, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(message);
    }
}

[CollectionDefinition(nameof(BridgeHostFixture))]
public sealed class BridgeCollection : ICollectionFixture<BridgeHostFixture>;
