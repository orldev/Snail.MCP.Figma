using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.MCP.Figma.Hosting;

#pragma warning disable MCP9005

/// <summary>The folder the client works in, as the client says it; its name is what the panel calls the agent.</summary>
/// <remarks>
/// Over stdio the server starts in the client's folder, so the launch folder names the agent. A server shared over HTTP
/// started in the folder of whoever launched it, which is nobody's project: the client's first file root is. Roots are
/// deprecated from the 2026-07-28 revision of the protocol (SEP-2577) and reach the server only within a session a client
/// opened with initialize, as clients of the 2025 revisions do; a client without them keeps the name it gave in
/// initialize, and one without that is numbered.
/// </remarks>
internal static class ClientFolder
{
    /// <summary>How long a client that lists roots gets to answer before the agent keeps the name it has.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>The name of the client's first <c>file:</c> root, or <c>null</c> when it lists none or does not answer.</summary>
    public static async Task<string?> NameAsync(McpServer server, CancellationToken cancellationToken)
    {
        if (server.ClientCapabilities?.Roots is null)
        {
            return null;
        }

        using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        patience.CancelAfter(Patience);

        try
        {
            var listed = await server.RequestRootsAsync(new ListRootsRequestParams(), patience.Token).ConfigureAwait(false);

            foreach (var root in listed.Roots)
            {
                if (Uri.TryCreate(root.Uri, UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(uri.LocalPath));

                    return string.IsNullOrWhiteSpace(name) ? null : name;
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (McpException)
        {
            return null;
        }
    }
}
