using System.Net;
using Snail.MCP.Figma.Adapters.Rest;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Rest;

/// <summary>Retry behaviour of <see cref="RestDocumentGateway"/> on 429: up to 3 attempts, <c>Retry-After</c> first, else 1s/2s backoff, and an immediate stop when <c>Retry-After</c> exceeds 30s; delays are faked.</summary>
public class RestDocumentGatewayTests
{
    private static ServerConfig Config() => new() { AccessToken = "figd_test-token" };

    private static (RestDocumentGateway Client, QueueHttpMessageHandler Handler, List<TimeSpan> Delays) Build(
        params HttpResponseMessage[] responses)
    {
        var (client, handler, delays, _) = BuildWithGate(new QuotaGate(), responses);

        return (client, handler, delays);
    }

    private static (RestDocumentGateway Client, QueueHttpMessageHandler Handler, List<TimeSpan> Delays, QuotaGate Gate) BuildWithGate(
        QuotaGate gate,
        params HttpResponseMessage[] responses)
    {
        var handler = new QueueHttpMessageHandler(responses);
        var delays = new List<TimeSpan>();

        Task FakeDelay(TimeSpan span, CancellationToken _)
        {
            delays.Add(span);

            return Task.CompletedTask;
        }

        var client = new RestDocumentGateway(
            new SingleHandlerHttpClientFactory(handler),
            Config(),
            NullLogger<RestDocumentGateway>.Instance,
            gate,
            FakeDelay);

        return (client, handler, delays, gate);
    }

    private static HttpResponseMessage TooManyRequests(string? retryAfterHeader = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"status\":429,\"err\":\"Rate limited\"}"),
        };

        if (retryAfterHeader is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfterHeader);
        }

        return response;
    }

    private static HttpResponseMessage Ok(string json = "{\"ok\":true}") =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public async Task RestDocumentGateway_SuccessAfterOneRetry_HonorsRetryAfterAndReturnsBody()
    {
        var (client, handler, delays) = Build(TooManyRequests(retryAfterHeader: "5"), Ok("{\"value\":42}"));

        var result = await client.GetAsync("/files/abc");

        Assert.Equal(42, (int)result["value"]!);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([TimeSpan.FromSeconds(5)], delays);
    }

    [Fact]
    public async Task NoRetryAfterHeader_FallsBackToExponentialBackoff_1sThen2s()
    {
        var (client, handler, delays) = Build(TooManyRequests(), TooManyRequests(), Ok());

        await client.GetAsync("/files/abc");

        Assert.Equal(3, handler.RequestCount);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task RestDocumentGateway_NonNumericRetryAfterHeader_FallsBackToExponentialBackoff()
    {
        var (client, handler, delays) = Build(TooManyRequests(retryAfterHeader: "not-a-number"), Ok());

        await client.GetAsync("/files/abc");

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task RestDocumentGateway_ThirdAttemptStillRateLimited_ThrowsWithoutAFourthCall()
    {
        var (client, handler, delays) = Build(TooManyRequests(), TooManyRequests(), TooManyRequests());

        var ex = await Assert.ThrowsAsync<PublishedSourceException>(() => client.GetAsync("/files/abc"));

        Assert.Equal(SourceFailure.RateLimited, ex.Failure);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task RestDocumentGateway_RetryAfterBeyondCap_StopsImmediatelyWithoutWaiting()
    {
        var (client, handler, delays) = Build(TooManyRequests(retryAfterHeader: "49787"));

        var ex = await Assert.ThrowsAsync<PublishedSourceException>(() => client.GetAsync("/files/abc"));

        Assert.Equal(SourceFailure.RateLimited, ex.Failure);
        Assert.Equal(TimeSpan.FromSeconds(49787), ex.RetryAfter);
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task RestDocumentGateway_RetryAfterExactlyAtCap_StillRetries()
    {
        var (client, handler, delays) = Build(TooManyRequests(retryAfterHeader: "30"), Ok());

        await client.GetAsync("/files/abc");

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([TimeSpan.FromSeconds(30)], delays);
    }

    [Fact]
    public async Task RestDocumentGateway_NonRateLimitError_DoesNotRetry()
    {
        var (client, handler, delays) = Build(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"status\":404,\"err\":\"Not found\"}"),
        });

        var ex = await Assert.ThrowsAsync<PublishedSourceException>(() => client.GetAsync("/files/abc"));

        Assert.Equal(SourceFailure.NotFound, ex.Failure);
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(delays);
    }

    /// <summary>Serves queued responses one per request; an extra request signals an extra retry.</summary>
    [Fact]
    public async Task RestDocumentGateway_QuotaExhausted_BlocksTheBucketAndSparesTheNetwork()
    {
        var gate = new QuotaGate();
        var (client, handler, _, _) = BuildWithGate(gate, TooManyRequests(retryAfterHeader: "49800"));

        var first = await Assert.ThrowsAsync<PublishedSourceException>(() => client.GetAsync("/images/abc?ids=1:2"));
        Assert.Equal(1, handler.RequestCount);

        var second = await Assert.ThrowsAsync<PublishedSourceException>(() => client.GetAsync("/images/abc?ids=3:4"));

        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("429", second.Message);
        Assert.Contains("circuit breaker", second.Message);
        Assert.NotNull(second.RetryAfter);
        Assert.Equal(SourceFailure.RateLimited, first.Failure);
    }

    [Fact]
    public async Task RestDocumentGateway_ImagesBucketBlocked_StillReadsTheFile()
    {
        var gate = new QuotaGate();
        var (client, handler, _, _) = BuildWithGate(
            gate,
            TooManyRequests(retryAfterHeader: "49800"),
            Ok("{\"name\":\"file\"}"));

        await Assert.ThrowsAsync<PublishedSourceException>(() => client.GetAsync("/images/abc"));

        var file = await client.GetAsync("/files/abc");

        Assert.Equal("file", file["name"]!.GetValue<string>());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task RestDocumentGateway_ShortRateLimit_IsCuredByRetriesWithoutArmingTheGate()
    {
        var gate = new QuotaGate();
        var (client, handler, _, _) = BuildWithGate(gate, TooManyRequests(retryAfterHeader: "5"), Ok(), Ok());

        await client.GetAsync("/files/abc");
        await client.GetAsync("/files/abc");

        Assert.Empty(gate.ActiveBlocks);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task RestDocumentGateway_SuccessfulCall_ReopensThePreviouslyBlockedBucket()
    {
        var gate = new QuotaGate();
        gate.Block("/images/abc", TimeSpan.FromMilliseconds(1));
        await Task.Delay(5);

        var (client, handler, _, _) = BuildWithGate(gate, Ok("{\"images\":{}}"));

        await client.GetAsync("/images/abc");

        Assert.Empty(gate.ActiveBlocks);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class QueueHttpMessageHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request #{RequestCount} to the Figma API — the response queue is empty.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
