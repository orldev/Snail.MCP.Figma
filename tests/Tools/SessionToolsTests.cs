using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Application.Documents;

using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>figma_execute_across_files fan-out: each file gets its own command, the active file and pinned target stay intact, and one failing file does not sink the rest.</summary>
[Collection(nameof(BridgeHostFixture))]
public class SessionToolsTests
{
    private readonly BridgeHostFixture _bridge;

    public SessionToolsTests(BridgeHostFixture bridge) => _bridge = bridge;

    private SessionTools Tools() => new(
        _bridge.Gateway,
        new ActiveDocumentTracker(),
        new CaptureStore(new ServerConfig(), NullLogger<CaptureStore>.Instance),
        NullLogger<SessionTools>.Instance);

    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private static string[] ExecutedCode(FakeEditorExtension plugin) => plugin.Received
        .Where(message => message["method"]?.GetValue<string>() == EditorCommands.ExecuteCode.Name)
        .Select(message => message["params"]!["code"]!.GetValue<string>())
        .ToArray();

    /// <summary>Waits until only this test's sessions remain in the registry; the fixture is shared per class.</summary>
    private Task OnlyOurSessionsAsync(int expected) => BridgeHostFixture.WaitUntilAsync(
        () => _bridge.Registry.Sessions.Count(session => session.IsOpen) == expected,
        $"the registry did not end up with exactly {expected} open sessions");

    [Fact]
    public async Task NamedFilesEachRunTheCodeInTheirOwnContext()
    {
        var first = NewFileKey();
        var second = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(first, "Web");
        await using var pluginB = await _bridge.ConnectPluginAsync(second, "Mobile");

        pluginA.Handlers[EditorCommands.ExecuteCode.Name] = _ => new JsonObject { ["pageCount"] = 3 };
        pluginB.Handlers[EditorCommands.ExecuteCode.Name] = _ => new JsonObject { ["pageCount"] = 7 };

        var response = Parse(await Tools().ExecuteAcrossFilesAsync(
            "return figma.root.children.length;",
            $"{first}, {second}"));

        Assert.True(response["ok"]!.GetValue<bool>());

        var data = response["data"]!;
        Assert.Equal(2, data["summary"]!["targeted"]!.GetValue<int>());
        Assert.Equal(2, data["summary"]!["succeeded"]!.GetValue<int>());
        Assert.Equal(0, data["summary"]!["failed"]!.GetValue<int>());

        Assert.Equal(3, data["results"]![first]!["result"]!["pageCount"]!.GetValue<int>());
        Assert.Equal(7, data["results"]![second]!["result"]!["pageCount"]!.GetValue<int>());
        Assert.Equal("Web", data["results"]![first]!["file_name"]!.GetValue<string>());

        Assert.Equal(["return figma.root.children.length;"], ExecutedCode(pluginA));
        Assert.Equal(["return figma.root.children.length;"], ExecutedCode(pluginB));
    }

    [Fact]
    public async Task PinnedTargetSurvivesTheFanOut()
    {
        var pinned = NewFileKey();
        var other = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(pinned);
        await using var pluginB = await _bridge.ConnectPluginAsync(other);

        _bridge.Registry.SetActiveFile(pinned, @lock: true);

        try
        {
            var data = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", other))["data"]!;

            Assert.Equal(pinned, data["active_file_key"]!.GetValue<string>());
            Assert.True(data["target_locked"]!.GetValue<bool>());
            Assert.Equal(pinned, _bridge.Registry.ActiveFileKey);

            Assert.Empty(ExecutedCode(pluginA));
            Assert.Single(ExecutedCode(pluginB));
        }
        finally
        {
            _bridge.Registry.Unlock();
        }
    }

    [Fact]
    public async Task TargetsMustBeStatedExplicitly()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("allFiles", response["hint"]!.GetValue<string>());
        Assert.Contains("figma_list_open_files", response["hint"]!.GetValue<string>());

        Assert.Empty(ExecutedCode(plugin));
    }

    [Fact]
    public async Task AllFilesReachesEveryConnectedFile()
    {
        var first = NewFileKey();
        var second = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(first);
        await using var pluginB = await _bridge.ConnectPluginAsync(second);
        await OnlyOurSessionsAsync(2);

        var data = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", allFiles: true))["data"]!;

        Assert.Equal(2, data["summary"]!["targeted"]!.GetValue<int>());
        Assert.Equal(2, data["summary"]!["succeeded"]!.GetValue<int>());
        Assert.Single(ExecutedCode(pluginA));
        Assert.Single(ExecutedCode(pluginB));
    }

    [Fact]
    public async Task OneFailingFileDoesNotSinkTheRest()
    {
        var healthy = NewFileKey();
        var broken = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(healthy);
        await using var pluginB = await _bridge.ConnectPluginAsync(broken);

        pluginB.Errors[EditorCommands.ExecuteCode.Name] = "ReferenceError: foo is not defined";

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return foo;", $"{healthy},{broken}"));

        Assert.True(response["ok"]!.GetValue<bool>());

        var data = response["data"]!;
        Assert.Equal(1, data["summary"]!["succeeded"]!.GetValue<int>());
        Assert.Equal(1, data["summary"]!["failed"]!.GetValue<int>());
        Assert.True(data["results"]![healthy]!["success"]!.GetValue<bool>());
        Assert.False(data["results"]![broken]!["success"]!.GetValue<bool>());
        Assert.Contains("ReferenceError", data["results"]![broken]!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task TotalFailureStillCarriesThePerFileBreakdown()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        plugin.Errors[EditorCommands.ExecuteCode.Name] = "TypeError: nope";

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return nope;", fileKey));

        Assert.False(response["ok"]!.GetValue<bool>());

        Assert.Contains("TypeError", response["data"]!["results"]![fileKey]!["error"]!.GetValue<string>());
        Assert.Equal(0, response["data"]!["summary"]!["succeeded"]!.GetValue<int>());
    }

    [Fact]
    public async Task DisconnectedFileKeysAreReportedInsteadOfSilentlyDropped()
    {
        var connected = NewFileKey();
        var closed = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(connected);

        var data = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", $"{connected},{closed}"))["data"]!;

        Assert.Equal(1, data["summary"]!["targeted"]!.GetValue<int>());
        Assert.Equal([closed], data["missing_file_keys"]!.AsArray().Select(key => key!.GetValue<string>()).ToArray());
    }

    [Fact]
    public async Task NoneOfTheNamedFilesConnectedIsAnErrorNotAnEmptySuccess()
    {
        var connected = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(connected);

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", NewFileKey()));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("None of the named files", response["error"]!.GetValue<string>());
        Assert.Empty(ExecutedCode(plugin));
    }

    [Fact]
    public async Task FileUrlsAreAcceptedWhereFileKeysAre()
    {
        var fileKey = "ABC123XYZ";
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        var data = Parse(await Tools().ExecuteAcrossFilesAsync(
            "return 1;",
            $"https://www.figma.com/design/{fileKey}/Design-System"))["data"]!;

        Assert.Equal(1, data["summary"]!["succeeded"]!.GetValue<int>());
        Assert.Single(ExecutedCode(plugin));
    }

    [Fact]
    public async Task WithoutAnyPluginTheAnswerIsTheUsualBridgeHint()
    {
        await OnlyOurSessionsAsync(0);

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", allFiles: true));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Snail Bridge", response["error"]!.GetValue<string>());
    }
}
