using System.Net.Http;
using UsagePill.Core;
using UsagePill.Polling;

namespace UsagePill.Tests;

public class BackoffPolicyTests
{
    private static BackoffPolicy Policy() => new(TimeSpan.FromMinutes(5));

    [Fact]
    public void SuccessUsesTheNormalInterval()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), Policy().NextDelay(null));
    }

    [Fact]
    public void RetryAfterIsHonouredExactly()
    {
        var policy = Policy();

        Assert.Equal(TimeSpan.FromSeconds(90), policy.NextDelay(new RateLimitedException(TimeSpan.FromSeconds(90))));
    }

    [Fact]
    public void ZeroRetryAfterIsFlooredAtSixtySeconds()
    {
        var policy = Policy();

        Assert.Equal(TimeSpan.FromSeconds(60), policy.NextDelay(new RateLimitedException(TimeSpan.Zero)));
    }

    [Fact]
    public void RetryAfterBelowTheFloorIsRaisedToTheFloor()
    {
        var policy = Policy();

        Assert.Equal(TimeSpan.FromSeconds(60), policy.NextDelay(new RateLimitedException(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public void RateLimitWithoutRetryAfterClimbsAndCapsAtSixtyMinutes()
    {
        var policy = Policy();
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 6; i++) delays.Add(policy.NextDelay(new RateLimitedException(null)));

        Assert.Equal(
            new[] { 5, 10, 20, 40, 60, 60 }.Select(m => TimeSpan.FromMinutes(m)).ToArray(),
            delays.ToArray());
    }

    [Fact]
    public void TransientErrorsClimbAndCapAtFifteenMinutes()
    {
        var policy = Policy();
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 5; i++) delays.Add(policy.NextDelay(new HttpRequestException("boom")));

        Assert.Equal(
            new[] { 1, 2, 4, 8, 15 }.Select(m => TimeSpan.FromMinutes(m)).ToArray(),
            delays.ToArray());
    }

    [Fact]
    public void SuccessResetsEveryLadder()
    {
        var policy = Policy();
        policy.NextDelay(new RateLimitedException(null));
        policy.NextDelay(new RateLimitedException(null));
        policy.NextDelay(new HttpRequestException("boom"));
        policy.NextDelay(new AuthExpiredException("x"));

        Assert.Equal(TimeSpan.FromMinutes(5), policy.NextDelay(null));
        Assert.Equal(TimeSpan.FromMinutes(5), policy.NextDelay(new RateLimitedException(null)));
        Assert.Equal(TimeSpan.FromMinutes(1), policy.NextDelay(new HttpRequestException("boom")));
        Assert.Equal(TimeSpan.FromSeconds(15), policy.NextDelay(new AuthExpiredException("x")));
    }

    [Fact]
    public void AlternatingFailureKindsClimbEachLadderIndependently()
    {
        var policy = Policy();

        // 429, transient, 429, transient: an overloaded endpoint fronted by a proxy can plausibly
        // alternate error kinds like this, and neither ladder's step may be reset by the other.
        Assert.Equal(TimeSpan.FromMinutes(5), policy.NextDelay(new RateLimitedException(null)));
        Assert.Equal(TimeSpan.FromMinutes(1), policy.NextDelay(new HttpRequestException("boom")));
        Assert.Equal(TimeSpan.FromMinutes(10), policy.NextDelay(new RateLimitedException(null)));
        Assert.Equal(TimeSpan.FromMinutes(2), policy.NextDelay(new HttpRequestException("boom")));
    }

    [Fact]
    public void AuthAndCredentialFailuresRetrySoonAndClimbToTheNormalInterval()
    {
        var policy = Policy();
        var delays = new List<TimeSpan>();

        // A rejected token and a missing credentials file share one ladder: both mean the pill
        // has no usable token, and both end the moment Claude Code writes a fresh one.
        for (var i = 0; i < 3; i++) delays.Add(policy.NextDelay(new AuthExpiredException("x")));
        for (var i = 0; i < 4; i++) delays.Add(policy.NextDelay(new NoCredentialsException("x")));

        Assert.Equal(
            new[] { 15, 30, 60, 120, 240, 300, 300 }.Select(s => TimeSpan.FromSeconds(s)).ToArray(),
            delays.ToArray());
    }
}
