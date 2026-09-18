using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tests;

/// <summary>The stdio contract of the real server process: stdout carries JSON-RPC and nothing else, the composition root boots, and every tool of the assembly reaches the model.</summary>
public class StdioContractTests
{
    private const string ProtocolVersion = "2025-06-18";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ServerAnswersTheHandshakeAndKeepsStdoutCleanOfEverythingButJsonRpc()
    {
        using var session = await ServerSession.StartAsync();

        var initialize = await session.CallAsync(1, "initialize", new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new { },
            clientInfo = new { name = "stdio-contract", version = "1.0" },
        });

        var serverInfo = initialize.GetProperty("result").GetProperty("serverInfo");

        Assert.Equal("Snail.MCP.Figma", serverInfo.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(serverInfo.GetProperty("version").GetString()));
        Assert.Equal(ServerInstructions.Text, initialize.GetProperty("result").GetProperty("instructions").GetString());

        await session.NotifyAsync("notifications/initialized");

        var listed = await session.CallAsync(2, "tools/list", new { });
        var names = listed.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ToolNamesOfAssembly(), names);

        var called = await session.CallAsync(3, "tools/call", new
        {
            name = "csharp_diagnose",
            arguments = new { probeDatabase = true },
        });

        var payload = called.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var report = JsonDocument.Parse(payload).RootElement;

        Assert.True(report.GetProperty("plugin").GetProperty("bundled").GetBoolean(), "the JS plugin did not reach the output directory");
        Assert.Equal("ok", report.GetProperty("cache").GetProperty("probe").GetString());

        var exit = await session.ShutdownAsync();

        Assert.Equal(0, exit);
        Assert.All(session.StdoutLines, line => Assert.Equal("2.0", session.ParseOrFail(line).GetProperty("jsonrpc").GetString()));
    }

    /// <summary>Tools are discovered by the SDK through attributes, so a misconfigured attribute is invisible to reflection-only guards.</summary>
    private static HashSet<string> ToolNamesOfAssembly() =>
        typeof(ToolDescriptions).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The server started as a separate process and spoken to over stdin/stdout, exactly as an MCP client does.</summary>
    private sealed class ServerSession : IDisposable
    {
        private readonly Process _process;
        private readonly string _dataDirectory;
        private readonly List<string> _stderr = [];

        private ServerSession(Process process, string dataDirectory)
        {
            _process = process;
            _dataDirectory = dataDirectory;
        }

        public List<string> StdoutLines { get; } = [];

        public static async Task<ServerSession> StartAsync()
        {
            var dataDirectory = Path.Combine(Path.GetTempPath(), $"snail-stdio-{Guid.NewGuid().ToString("N")[..8]}");
            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Snail.MCP.Figma.dll"));
            start.Environment["SNAIL_MCP_FIGMA_DATA_DIRECTORY"] = dataDirectory;
            start.Environment["SNAIL_MCP_FIGMA_IDLE_TIMEOUT_MINUTES"] = "0";
            start.Environment["SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_START"] = "9400";
            start.Environment["SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_END"] = "9410";
            start.Environment.Remove("SNAIL_MCP_FIGMA_ACCESS_TOKEN");
            start.Environment.Remove("SNAIL_MCP_FIGMA_CONFIG");

            var session = new ServerSession(Process.Start(start)!, dataDirectory);
            session.DrainStderr();

            return await Task.FromResult(session);
        }

        public async Task<JsonElement> CallAsync(int id, string method, object parameters)
        {
            await NotifyAsync(method, parameters, id);

            return await ReadAsync();
        }

        public async Task NotifyAsync(string method, object? parameters = null, int? id = null)
        {
            var envelope = new Dictionary<string, object> { ["jsonrpc"] = "2.0", ["method"] = method };

            if (id is { } value) envelope["id"] = value;
            if (parameters is not null) envelope["params"] = parameters;

            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(envelope));
            await _process.StandardInput.FlushAsync();
        }

        public async Task<int> ShutdownAsync()
        {
            _process.StandardInput.Close();

            using var deadline = new CancellationTokenSource(Timeout);

            while (await _process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length > 0) StdoutLines.Add(line);
            }

            await _process.WaitForExitAsync(deadline.Token);

            return _process.ExitCode;
        }

        /// <summary>stdio carries JSON-RPC only: a single stray Console.Write breaks every MCP client, so the line itself is reported.</summary>
        public JsonElement ParseOrFail(string line)
        {
            try
            {
                return JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                Assert.Fail($"stdout carries a line that is not JSON-RPC: {line}\nstderr:\n{Stderr()}");

                throw;
            }
        }

        private string Stderr()
        {
            lock (_stderr) return string.Join('\n', _stderr);
        }

        private async Task<JsonElement> ReadAsync()
        {
            using var deadline = new CancellationTokenSource(Timeout);

            while (await _process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length == 0) continue;

                StdoutLines.Add(line);

                var message = ParseOrFail(line);

                if (message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _))
                {
                    return message;
                }
            }

            throw new InvalidOperationException($"the server closed stdout without answering. stderr:\n{Stderr()}");
        }

        private void DrainStderr() => _ = Task.Run(async () =>
        {
            while (await _process.StandardError.ReadLineAsync() is { } line)
            {
                lock (_stderr) _stderr.Add(line);
            }
        });

        public void Dispose()
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);

            _process.Dispose();

            if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
        }
    }
}
