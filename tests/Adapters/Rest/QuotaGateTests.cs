using Snail.MCP.Figma.Adapters.Rest;

namespace Snail.MCP.Figma.Tests.Adapters.Rest;

/// <summary>Quota circuit breaker: while Figma asks to wait for hours, requests to the closed bucket never hit the network, but sibling buckets keep working.</summary>
public class QuotaGateTests
{
    [Theory]
    [InlineData("/files/abc", "files")]
    [InlineData("/files/abc?depth=1", "files")]
    [InlineData("/files/abc/nodes?ids=1:2", "files/nodes")]
    [InlineData("/files/abc/versions", "files/versions")]
    [InlineData("/images/abc?ids=1:2&scale=2", "images")]
    [InlineData("/me", "me")]
    public void BucketIgnoresFileKeyButKeepsSubresource(string endpoint, string expected) =>
        Assert.Equal(expected, QuotaGate.BucketOf(endpoint));

    [Fact]
    public void BlockedBucketDoesNotLeakToItsNeighbours()
    {
        var gate = new QuotaGate();
        gate.Block("/images/abc", TimeSpan.FromHours(13));

        Assert.NotNull(gate.RemainingFor("/images/xyz?ids=1:2"));
        Assert.Null(gate.RemainingFor("/files/abc"));
        Assert.Null(gate.RemainingFor("/files/abc/versions"));
    }

    [Fact]
    public void BlockExpiresOnItsOwnAndForgetsTheBucket()
    {
        var now = DateTimeOffset.UtcNow;
        var gate = new QuotaGate(() => now);

        gate.Block("/images/abc", TimeSpan.FromMinutes(30));
        Assert.NotNull(gate.RemainingFor("/images/abc"));

        now = now.AddMinutes(31);

        Assert.Null(gate.RemainingFor("/images/abc"));
        Assert.Empty(gate.ActiveBlocks);
    }

    [Fact]
    public void SuccessReleasesTheBucketEvenBeforeRetryAfterRunsOut()
    {
        var gate = new QuotaGate();
        gate.Block("/images/abc", TimeSpan.FromHours(13));

        gate.Release("/images/abc");

        Assert.Null(gate.RemainingFor("/images/abc"));
    }

    [Fact]
    public void LongerBlockWins()
    {
        var gate = new QuotaGate();
        gate.Block("/images/abc", TimeSpan.FromHours(13));
        gate.Block("/images/abc", TimeSpan.FromMinutes(1));

        Assert.True(gate.RemainingFor("/images/abc") > TimeSpan.FromHours(12));
    }
}
