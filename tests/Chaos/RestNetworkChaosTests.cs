using System.Net;
using Snail.MCP.Figma.Adapters.Persistence.Sqlite;
using Snail.MCP.Figma.Adapters.Rest;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Chaos;

/// <summary>The REST path when the network, not Figma, is what fails: a dropped connection, an HttpClient timeout, a 503 from the edge.</summary>
/// <remarks>Every failure the server plans for is a <see cref="PublishedSourceException"/>; the transport's own exceptions pass every catch untouched.</remarks>
[Collection(nameof(BridgeHostFixture))]
public class RestNetworkChaosTests(BridgeHostFixture bridge, SnapshotDatabaseFixture cacheFixture) : IClassFixture<SnapshotDatabaseFixture>
{
    private const string FileUrl = "https://www.figma.com/design/ABC123XYZ/Design-System";

    private readonly BridgeHostFixture _bridge = bridge;

    private readonly SnapshotDatabaseFixture _cacheFixture = cacheFixture;

    private readonly string _fileKey = $"chaos-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Breaks the stale fallback: a snapshot is in the cache, the network drops, and the reader throws instead of serving it as <c>cache-stale</c>.</summary>
    /// <remarks>DocumentReader.ReadAsync falls back only on <see cref="PublishedSourceException"/>; the gateway let an HttpRequestException or the TaskCanceledException of HttpClient.Timeout through as they were.</remarks>
    [Theory]
    [InlineData("connection refused")]
    [InlineData("http client timeout")]
    public async Task DocumentReader_NetworkFailsWithSnapshotCached_ServesStaleSnapshot(string failure)
    {
        var handler = new ScriptedHandler(
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleFile) },
            () => throw Transport(failure));
        var reader = Reader(Gateway(handler));
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive, MaxDepth = 3 };
        await reader.ReadFileAsync(_fileKey, new DocumentReadOptions { Depth = 3 }, request);

        var stale = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions { Depth = 3 }, request);

        Assert.True(stale.IsStale);
    }

    /// <summary>Breaks the error contract: a dropped connection escapes the tool's guard, so the agent gets the SDK's generic "An error occurred invoking" without the cause or a hint.</summary>
    [Fact]
    public async Task CommentTools_NetworkDrops_AnswersWithErrorEnvelope()
    {
        var gateway = Gateway(new ThrowingHandler(() => new HttpRequestException("Connection refused (api.figma.com:443)")));
        var tools = new CommentTools(_bridge.Gateway, gateway, new ActiveDocumentTracker(), NullLogger<CommentTools>.Instance);

        var exception = await Record.ExceptionAsync(() => tools.GetCommentsAsync(FileUrl));

        Assert.Null(exception);
    }

    /// <summary>Breaks resilience to a passing outage: a 503 from Figma's edge, or one refused connection, fails the call at once although the retry loop is right there for 429.</summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task RestDocumentGateway_TransientServerError_RetriesAndSucceeds(HttpStatusCode transient)
    {
        var handler = new ScriptedHandler(
            () => new HttpResponseMessage(transient) { Content = new StringContent("<html>upstream</html>") },
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"value":42}""") });

        var result = await Gateway(handler).GetAsync("/files/abc");

        Assert.Equal(42, (int)result["value"]!);
    }

    [Fact]
    public async Task RestDocumentGateway_ConnectionRefusedOnce_RetriesAndSucceeds()
    {
        var handler = new ScriptedHandler(
            () => throw new HttpRequestException("Connection refused"),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"value":42}""") });

        var result = await Gateway(handler).GetAsync("/files/abc");

        Assert.Equal(42, (int)result["value"]!);
    }

    private DocumentReader Reader(IPublishedDocumentSource api)
    {
        var cacheOptions = new CacheOptions { Enabled = true, TtlHours = 1, FreshnessSeconds = 0 };
        var cache = new SqliteNodeSnapshotStore(_cacheFixture.Database, cacheOptions, NullLogger<SqliteNodeSnapshotStore>.Instance);
        var images = new SqliteRenderSnapshotStore(_cacheFixture.Database, cacheOptions, NullLogger<SqliteRenderSnapshotStore>.Instance);
        var versions = new SqliteDocumentVersionLedger(_cacheFixture.Database, cache, images, NullLogger<SqliteDocumentVersionLedger>.Instance);
        var sanitizer = new NodeSanitizer(new SanitizationOptions { LogMetrics = false }, NullLogger<NodeSanitizer>.Instance);

        return new DocumentReader(api, cache, versions, sanitizer, cacheOptions, NullLogger<DocumentReader>.Instance);
    }

    private const string SampleFile = """{"name":"File","version":"v1","lastModified":"2026-10-16T00:00:00Z","document":{"id":"0:0","name":"Document","type":"DOCUMENT","children":[]}}""";

    private static Exception Transport(string failure) => failure switch
    {
        "connection refused" => new HttpRequestException("Connection refused (api.figma.com:443)"),
        _ => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.", new TimeoutException()),
    };

    private static RestDocumentGateway Gateway(HttpMessageHandler handler) => new(
        new OneHandlerFactory(handler),
        new ServerConfig { AccessToken = "figd_chaos-token" },
        NullLogger<RestDocumentGateway>.Instance,
        new QuotaGate(),
        static (_, _) => Task.CompletedTask);

    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        private int _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var answer = answers[Math.Min(_next++, answers.Length - 1)];

            return Task.FromResult(answer());
        }
    }

    private sealed class ThrowingHandler(Func<Exception> failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(failure());
    }

    private sealed class OneHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
