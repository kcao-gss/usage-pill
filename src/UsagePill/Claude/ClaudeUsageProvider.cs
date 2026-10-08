using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>Reads Claude subscription limits from the OAuth usage endpoint.</summary>
public sealed class ClaudeUsageProvider : IUsageProvider
{
    public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";

    // The endpoint picks a rate limit bucket from the User-Agent. A request that does not look
    // like Claude Code lands in a bucket that 429s after a handful of calls and then stays rate
    // limited for hours, with no Retry-After to anchor a backoff against. The version is not
    // validated, so a constant is enough: no process launch, and no woken WSL distribution.
    private const string UserAgent = "claude-code/2.1.270";

    private readonly IClaudeCredentialSource _credentials;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    // The last token the endpoint refused, and whether it refused it for the missing scope.
    private (string Token, bool MissingScope)? _rejection;

    public ClaudeUsageProvider(IClaudeCredentialSource credentials, HttpClient http, TimeProvider clock)
    {
        _credentials = credentials;
        _http = http;
        _clock = clock;
    }

    public string ProviderId => "claude";

    public string DisplayName => "Claude";

    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        // Re-read every poll: Claude Code refreshes the token in place.
        var credentials = _credentials.Read();

        if (!credentials.HasProfileScope)
        {
            // The endpoint would refuse it, so spare a request against a rate limited endpoint.
            // Invalidate anyway: a properly signed-in Claude Code elsewhere should take over.
            _credentials.Invalidate();
            throw new MissingScopeException("The access token lacks the user:profile scope.");
        }

        // A dead token only collects refusals, and the endpoint answers a dead token that keeps
        // asking with 429. That 429 then holds the next poll off for up to an hour, long after
        // Claude Code has refreshed the token. So a token that is past its expiry, or that the
        // endpoint already refused, is reported without a request until a new token replaces it.
        if (credentials.ExpiresAt is { } expiresAt && expiresAt <= _clock.GetUtcNow())
        {
            throw new AuthExpiredException("The access token is past its expiry.");
        }

        if (_rejection is { } rejection && rejection.Token == credentials.AccessToken)
        {
            throw rejection.MissingScope
                ? new MissingScopeException("The usage endpoint already refused this token for lacking the user:profile scope.")
                : new AuthExpiredException("The usage endpoint already rejected this access token.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("User-Agent", UserAgent);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                // This token is dead. Let the source look at every location again, so a
                // second Claude Code, in WSL or on Windows, can take over on the next poll.
                _credentials.Invalidate();

                // A file without a scopes list still reaches the endpoint, which names the
                // missing scope in its 403 body. Restarting Claude Code would not fix that.
                var missingScope = response.StatusCode == HttpStatusCode.Forbidden &&
                    (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Contains(ClaudeCredentials.ProfileScope, StringComparison.Ordinal);
                _rejection = (credentials.AccessToken, missingScope);

                if (missingScope)
                {
                    throw new MissingScopeException("The usage endpoint refused a token without the user:profile scope.");
                }

                throw new AuthExpiredException("The usage endpoint rejected the access token.");
            case HttpStatusCode.TooManyRequests:
                throw new RateLimitedException(ReadRetryAfter(response));
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ClaudeUsageJson.Parse(json, _clock.GetUtcNow());
    }

    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta) return delta;
        if (header.Date is { } date)
        {
            var wait = date - _clock.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }
}
