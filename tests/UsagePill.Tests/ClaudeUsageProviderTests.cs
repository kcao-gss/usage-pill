using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using UsagePill.Claude;
using UsagePill.Core;

namespace UsagePill.Tests;

public class ClaudeUsageProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("usagepill").FullName;

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

    private ClaudeCredentialStore Store()
    {
        var path = Path.Combine(_dir, ".credentials.json");
        File.WriteAllText(path, """{ "claudeAiOauth": { "accessToken": "tok-123", "expiresAt": 1789423372964 } }""");
        return new ClaudeCredentialStore(path);
    }

    private static ClaudeUsageProvider Provider(ClaudeCredentialStore store, StubHandler handler) =>
        new(store, new HttpClient(handler), TimeProvider.System);

    [Fact]
    public async Task SendsTheBearerTokenAndTheBetaHeader()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "limits": [ { "kind": "session", "percent": 90, "severity": "critical" } ] }"""),
        });

        var snapshot = await Provider(Store(), handler).FetchAsync(CancellationToken.None);

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
            () => Provider(Store(), handler).FetchAsync(CancellationToken.None));
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
            () => Provider(Store(), handler).FetchAsync(CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(90), error.RetryAfter);
    }

    [Fact]
    public async Task ServerErrorBecomesHttpRequestException()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Provider(Store(), handler).FetchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingCredentialsPropagate()
    {
        var store = new ClaudeCredentialStore(Path.Combine(_dir, "absent.json"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<NoCredentialsException>(
            () => Provider(store, handler).FetchAsync(CancellationToken.None));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
