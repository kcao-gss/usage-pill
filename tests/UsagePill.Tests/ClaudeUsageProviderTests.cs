using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using UsagePill.Claude;
using UsagePill.Core;

namespace UsagePill.Tests;

public class ClaudeUsageProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class StubSource : IClaudeCredentialSource
    {
        private readonly Func<ClaudeCredentials> _read;
        public int Invalidations { get; private set; }

        public StubSource(Func<ClaudeCredentials> read) => _read = read;

        public ClaudeCredentials Read() => _read();

        public void Invalidate() => Invalidations++;
    }

    private static StubSource Source() =>
        new(() => new ClaudeCredentials("tok-123", DateTimeOffset.UnixEpoch));

    private static ClaudeUsageProvider Provider(IClaudeCredentialSource source, StubHandler handler) =>
        Provider(source, handler, TimeProvider.System);

    private static ClaudeUsageProvider Provider(IClaudeCredentialSource source, StubHandler handler, TimeProvider clock) =>
        new(source, new HttpClient(handler), clock);

    [Fact]
    public async Task SendsTheBearerTokenAndTheBetaHeader()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "limits": [ { "kind": "session", "percent": 90, "severity": "critical" } ] }"""),
        });

        var snapshot = await Provider(Source(), handler).FetchAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        // Literal, not ClaudeUsageProvider.UsageUrl: a typo in the constant must fail here.
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-123", handler.LastRequest!.Headers.Authorization!.Parameter);
        Assert.Equal("oauth-2025-04-20", handler.LastRequest!.Headers.GetValues("anthropic-beta").Single());
        Assert.Equal(90, snapshot.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public async Task UnauthorizedBecomesAuthExpired()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<AuthExpiredException>(
            () => Provider(Source(), handler).FetchAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARejectedTokenSendsTheSourceLookingAgain(HttpStatusCode status)
    {
        // Otherwise a dead Windows token would keep being retried while a signed-in
        // Claude Code in WSL sits unused.
        var source = Source();
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<AuthExpiredException>(
            () => Provider(source, handler).FetchAsync(CancellationToken.None));

        Assert.Equal(1, source.Invalidations);
    }

    [Fact]
    public async Task ARateLimitLeavesTheChosenSourceAlone()
    {
        var source = Source();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<RateLimitedException>(
            () => Provider(source, handler).FetchAsync(CancellationToken.None));

        Assert.Equal(0, source.Invalidations);
    }

    [Fact]
    public async Task TooManyRequestsCarriesRetryAfter()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });

        var error = await Assert.ThrowsAsync<RateLimitedException>(
            () => Provider(Source(), handler).FetchAsync(CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(90), error.RetryAfter);
    }

    [Fact]
    public async Task TooManyRequestsWithAnHttpDateUsesTheInjectedClock()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(1));
            return response;
        });

        var error = await Assert.ThrowsAsync<RateLimitedException>(
            () => Provider(Source(), handler, clock).FetchAsync(CancellationToken.None));

        Assert.Equal(TimeSpan.FromMinutes(1), error.RetryAfter);
    }

    [Fact]
    public async Task ServerErrorBecomesHttpRequestException()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Provider(Source(), handler).FetchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingCredentialsPropagate()
    {
        var source = new StubSource(() => throw new NoCredentialsException("nowhere"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<NoCredentialsException>(
            () => Provider(source, handler).FetchAsync(CancellationToken.None));
    }
}
