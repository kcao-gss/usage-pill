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
        public int Requests { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            Requests++;
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class StubSource : IClaudeCredentialSource
    {
        private readonly Func<ClaudeCredentials> _read;
        public int Invalidations { get; private set; }
        public int Refreshes { get; private set; }

        public StubSource(Func<ClaudeCredentials> read) => _read = read;

        public ClaudeCredentials Read() => _read();

        public void Invalidate() => Invalidations++;

        /// <summary>Behaves like a file nobody else touches: the grant replaces the tokens.</summary>
        public async Task<ClaudeCredentials> RefreshAsync(
            ClaudeCredentials current,
            Func<string, CancellationToken, Task<ClaudeTokenGrant>> exchange,
            CancellationToken ct)
        {
            Refreshes++;
            var grant = await exchange(current.RefreshToken!, ct);
            return current with
            {
                AccessToken = grant.AccessToken,
                ExpiresAt = grant.ExpiresAt,
                RefreshToken = grant.RefreshToken ?? current.RefreshToken,
            };
        }
    }

    private static StubSource Source() =>
        new(() => new ClaudeCredentials("tok-123", DateTimeOffset.MaxValue));

    private static ClaudeUsageProvider Provider(IClaudeCredentialSource source, StubHandler handler) =>
        Provider(source, handler, TimeProvider.System);

    private static ClaudeUsageProvider Provider(IClaudeCredentialSource source, StubHandler handler, TimeProvider clock) =>
        new(source, new HttpClient(handler), clock);

    [Fact]
    public async Task SendsTheBearerTokenAndTheClaudeCodeHeaders()
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
        // Without this the endpoint answers from a bucket that 429s within a few polls.
        Assert.Equal("claude-code/2.1.270", handler.LastRequest!.Headers.UserAgent.ToString());
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
    public async Task ATokenWithoutTheProfileScopeIsReportedWithoutARequest()
    {
        var source = new StubSource(() => new ClaudeCredentials("tok-123", DateTimeOffset.UnixEpoch, HasProfileScope: false));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<MissingScopeException>(
            () => Provider(source, handler).FetchAsync(CancellationToken.None));

        Assert.Null(handler.LastRequest);
        Assert.Equal(1, source.Invalidations);
    }

    [Fact]
    public async Task AForbiddenNamingTheProfileScopeBecomesMissingScope()
    {
        var source = Source();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""
                { "type": "error", "error": { "type": "permission_error",
                  "message": "OAuth token does not meet scope requirement user:profile" } }
                """),
        });

        await Assert.ThrowsAsync<MissingScopeException>(
            () => Provider(source, handler).FetchAsync(CancellationToken.None));

        Assert.Equal(1, source.Invalidations);
    }

    [Fact]
    public async Task ATokenAtItsExpiryWithoutARefreshTokenIsReportedWithoutARequest()
    {
        var now = new DateTimeOffset(2026, 10, 7, 19, 58, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var source = new StubSource(() => new ClaudeCredentials("tok-123", now));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<AuthExpiredException>(
            () => Provider(source, handler, clock).FetchAsync(CancellationToken.None));

        Assert.Equal(0, handler.Requests);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 19, 58, 0, TimeSpan.Zero);

    private static StubSource ExpiredSource(Func<string> refreshToken) =>
        new(() => new ClaudeCredentials("tok-old", Now, RefreshToken: refreshToken()));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public async Task AnExpiredTokenIsRenewedWithClaudeCodesClientAndTheNewTokenIsSent()
    {
        var source = ExpiredSource(() => "ref-old");
        string? tokenRequestBody = null;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.ToString() == ClaudeUsageProvider.TokenUrl)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                // The token endpoint 429s a claude-code/ agent outright.
                Assert.StartsWith("usage-pill/", request.Headers.UserAgent.ToString());
                tokenRequestBody = request.Content!.ReadAsStringAsync().Result;
                return Json("""{ "access_token": "tok-new", "refresh_token": "ref-new", "expires_in": 28800 }""");
            }

            Assert.Equal("tok-new", request.Headers.Authorization!.Parameter);
            return Json("{}");
        });

        await Provider(source, handler, new FakeTimeProvider(Now)).FetchAsync(CancellationToken.None);

        Assert.Equal(
            "grant_type=refresh_token&refresh_token=ref-old&client_id=9d1c250a-e61b-44d9-88ed-5944d1962f5e",
            tokenRequestBody);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task TheGrantExpiresTheGrantedNumberOfSecondsFromNow()
    {
        ClaudeTokenGrant? grant = null;
        var source = new CapturingSource(new ClaudeCredentials("tok-old", Now, RefreshToken: "ref-old"), g => grant = g);
        var handler = new StubHandler(request => request.Method == HttpMethod.Post
            ? Json("""{ "access_token": "tok-new", "expires_in": 28800 }""")
            : Json("{}"));

        await Provider(source, handler, new FakeTimeProvider(Now)).FetchAsync(CancellationToken.None);

        Assert.Equal(new ClaudeTokenGrant("tok-new", null, Now.AddHours(8)), grant);
    }

    private sealed class CapturingSource(ClaudeCredentials current, Action<ClaudeTokenGrant> capture) : IClaudeCredentialSource
    {
        public ClaudeCredentials Read() => current;

        public void Invalidate() { }

        public async Task<ClaudeCredentials> RefreshAsync(
            ClaudeCredentials expired,
            Func<string, CancellationToken, Task<ClaudeTokenGrant>> exchange,
            CancellationToken ct)
        {
            var grant = await exchange(expired.RefreshToken!, ct);
            capture(grant);
            return expired with { AccessToken = grant.AccessToken, ExpiresAt = grant.ExpiresAt };
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARefusedRefreshTokenIsNotSentAgainButANewSignInIs(HttpStatusCode refusal)
    {
        var refreshToken = "ref-revoked";
        var source = ExpiredSource(() => refreshToken);
        var handler = new StubHandler(request => request.Method == HttpMethod.Post
            ? request.Content!.ReadAsStringAsync().Result.Contains("ref-revoked")
                ? new HttpResponseMessage(refusal)
                : Json("""{ "access_token": "tok-new", "expires_in": 3600 }""")
            : Json("{}"));
        var provider = Provider(source, handler, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<AuthExpiredException>(() => provider.FetchAsync(CancellationToken.None));
        await Assert.ThrowsAsync<AuthExpiredException>(() => provider.FetchAsync(CancellationToken.None));
        Assert.Equal(1, handler.Requests);

        // The user ran /login, which wrote a new refresh token.
        refreshToken = "ref-signed-in";
        await provider.FetchAsync(CancellationToken.None);
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task ARateLimitedRefreshCarriesRetryAfterAndIsTriedAgain()
    {
        var source = ExpiredSource(() => "ref-old");
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });
        var provider = Provider(source, handler, new FakeTimeProvider(Now));

        var limited = await Assert.ThrowsAsync<RateLimitedException>(() => provider.FetchAsync(CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(90), limited.RetryAfter);

        await Assert.ThrowsAsync<RateLimitedException>(() => provider.FetchAsync(CancellationToken.None));
        Assert.Equal(2, source.Refreshes);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "expires_in": 3600 }""")]
    [InlineData("""{ "access_token": "", "expires_in": 3600 }""")]
    [InlineData("""{ "access_token": "tok-new" }""")]
    [InlineData("""{ "access_token": "tok-new", "expires_in": 0 }""")]
    public async Task AGrantWithoutAUsableTokenAndExpiryIsANetworkError(string body)
    {
        var source = ExpiredSource(() => "ref-old");
        var handler = new StubHandler(_ => Json(body));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Provider(source, handler, new FakeTimeProvider(Now)).FetchAsync(CancellationToken.None));

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ATokenOneSecondBeforeItsExpiryIsStillSent()
    {
        var now = new DateTimeOffset(2026, 10, 7, 19, 58, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var source = new StubSource(() => new ClaudeCredentials("tok-123", now.AddSeconds(1)));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        await Provider(source, handler, clock).FetchAsync(CancellationToken.None);

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ARejectedTokenIsNotSentAgainButItsReplacementIs()
    {
        var token = "tok-dead";
        var source = new StubSource(() => new ClaudeCredentials(token, DateTimeOffset.MaxValue));
        var handler = new StubHandler(request => request.Headers.Authorization!.Parameter == "tok-dead"
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var provider = Provider(source, handler);

        await Assert.ThrowsAsync<AuthExpiredException>(() => provider.FetchAsync(CancellationToken.None));
        await Assert.ThrowsAsync<AuthExpiredException>(() => provider.FetchAsync(CancellationToken.None));
        Assert.Equal(1, handler.Requests);

        // Claude Code refreshed the file in place.
        token = "tok-fresh";
        await provider.FetchAsync(CancellationToken.None);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task ATokenRefusedForTheProfileScopeStaysMissingScopeWithoutARequest()
    {
        var source = Source();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{ "error": { "message": "OAuth token does not meet scope requirement user:profile" } }"""),
        });
        var provider = Provider(source, handler);

        await Assert.ThrowsAsync<MissingScopeException>(() => provider.FetchAsync(CancellationToken.None));
        await Assert.ThrowsAsync<MissingScopeException>(() => provider.FetchAsync(CancellationToken.None));

        Assert.Equal(1, handler.Requests);
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
