using Snail.MCP.Figma.Adapters.Editor;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>What one plugin connection promises the panel about the order of what it is told.</summary>
[Collection(nameof(BridgeHostFixture))]
public class EditorSessionTests(BridgeHostFixture bridge)
{
    /// <summary>A notice is composed where it is sent: the second one cannot read the state before the first is on the wire, so the panel never takes an older picture for the latest.</summary>
    /// <remarks>Composed first and sent later, "bound" from an adoption overtook "not bound" from the claim that followed it, and the panel showed an agent that had left — once in a few hundred runs of the target tests under load.</remarks>
    [Fact]
    public async Task EditorSession_TwoNoticesAtOnce_SecondIsComposedAfterTheFirstIsSent()
    {
        var fileKey = $"order-{Guid.NewGuid():N}"[..20];
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);
        var session = bridge.Registry.Find(fileKey)!;
        using var firstMayFinish = new ManualResetEventSlim();
        var firstComposing = new TaskCompletionSource();
        var isSecondComposed = false;

        var first = Task.Run(() => session.SendAsync(
            () =>
            {
                firstComposing.SetResult();
                firstMayFinish.Wait(TimeSpan.FromSeconds(5));

                return EditorWireProtocol.TargetBound(true);
            },
            CancellationToken.None));
        await firstComposing.Task;
        var second = session.SendAsync(
            () =>
            {
                isSecondComposed = true;

                return EditorWireProtocol.TargetBound(false);
            },
            CancellationToken.None);
        await Task.Delay(100);

        Assert.False(isSecondComposed, "the second notice read the state while the first was still being composed");

        firstMayFinish.Set();
        await Task.WhenAll(first, second);
        await BridgeHostFixture.WaitUntilAsync(() => Bound(plugin).Count == 2, "the panel did not receive both notices");

        Assert.Equal([true, false], Bound(plugin));
    }

    private static List<bool> Bound(FakeEditorExtension plugin) => [.. plugin.Received
        .Where(message => message["type"]?.GetValue<string>() == "TARGET_BOUND")
        .Select(message => message["data"]!["bound"]!.GetValue<bool>())];
}
