namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Who the configured REST token belongs to — the cheapest way to prove the token works.</summary>
public interface IApiIdentity
{
    /// <summary>Throws <see cref="PublishedSourceException"/> when the token is missing, expired or revoked.</summary>
    Task<JsonNode> GetMeAsync(CancellationToken cancellationToken = default);
}
