using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>
/// Reads Claude subscription limits from the OAuth usage endpoint, and renews an expired
/// access token with the refresh token in the same credentials file.
/// </summary>
public sealed class ClaudeUsageProvider : IUsageProvider
{
    public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    public const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string BetaHeader = "oauth-2025-04-20";

    // Claude Code's public OAuth client. A refresh token only redeems under the client it was
    // issued to, and every token in the credentials file was issued to this one.
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    // The endpoint picks a rate limit bucket from the User-Agent. A request that does not look
    // like Claude Code lands in a bucket that 429s after a handful of calls and then stays rate
    // limited for hours, with no Retry-After to anchor a backoff against. The version is not
    // validated, so a constant is enough: no process launch, and no woken WSL distribution.
    private const string UserAgent = "claude-code/2.1.270";

    // The token endpoint is the opposite: it answers claude-code/, browser and curl agents with
    // 429 before it even looks at the refresh token, and serves a client that names itself.
    private static readonly string TokenUserAgent =
        $"usage-pill/{typeof(ClaudeUsageProvider).Assembly.GetName().Version?.ToString(3)}";

    private readonly IClaudeCredentialSource _credentials;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    // The last token the endpoint refused, and whether it refused it for the missing scope.
    private (string Token, bool MissingScope)? _rejection;

    // The last refresh token the token endpoint refused. Asking again with it cannot succeed;
    // only a new sign-in, which writes a new refresh token, can.
    private string? _refusedRefreshToken;

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

        // An expired token is renewed only once it is past its expiry, never ahead of it:
        // Claude Code and other tools refresh the same file, and each refresh can make the
        // refresh token they all share invalid. Waiting leaves the refresh to them while they run.
        if (IsExpired(credentials))
        {
            credentials = await RenewAsync(credentials, ct).ConfigureAwait(false);
        }

        // The endpoint answers a dead token that keeps asking with 429, and that 429 then holds
        // the next poll off for up to an hour. So a token the endpoint already refused is
        // reported without a request until a new token replaces it.
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

    private bool IsExpired(ClaudeCredentials credentials) =>
        credentials.ExpiresAt is { } expiresAt && expiresAt <= _clock.GetUtcNow();

    /// <summary>
    /// Trades the refresh token for a new token pair, written back into the credentials file.
    /// A token that cannot be renewed is reported as expired without a request.
    /// </summary>
    private async Task<ClaudeCredentials> RenewAsync(ClaudeCredentials expired, CancellationToken ct)
    {
        if (expired.RefreshToken is null)
        {
            throw new AuthExpiredException("The access token is past its expiry and the credentials file holds no refresh token.");
        }

        if (expired.RefreshToken == _refusedRefreshToken)
        {
            throw new AuthExpiredException("The access token is past its expiry and the token endpoint already refused its refresh token.");
        }

        var renewed = await _credentials.RefreshAsync(expired, ExchangeAsync, ct).ConfigureAwait(false);
        if (IsExpired(renewed))
        {
            throw new AuthExpiredException("The access token is still past its expiry after the refresh.");
        }

        return renewed;
    }

    private async Task<ClaudeTokenGrant> ExchangeAsync(string refreshToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("client_id", ClientId),
            ]),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("User-Agent", TokenUserAgent);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.BadRequest:
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                // Revoked, already spent by another program, or signed out. Only a new sign-in fixes it.
                _refusedRefreshToken = refreshToken;
                throw new AuthExpiredException("The token endpoint refused the refresh token.");
            case HttpStatusCode.TooManyRequests:
                throw new RateLimitedException(ReadRetryAfter(response));
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseGrant(json);
    }

    private ClaudeTokenGrant ParseGrant(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("access_token", out var accessToken) &&
                accessToken.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(accessToken.GetString()) &&
                root.TryGetProperty("expires_in", out var expiresIn) &&
                expiresIn.ValueKind == JsonValueKind.Number &&
                expiresIn.TryGetInt64(out var seconds) &&
                seconds > 0)
            {
                // A missing refresh_token means the old one stays valid.
                var refreshToken = root.TryGetProperty("refresh_token", out var rotated) &&
                    rotated.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(rotated.GetString())
                        ? rotated.GetString()
                        : null;

                return new ClaudeTokenGrant(accessToken.GetString()!, refreshToken, _clock.GetUtcNow().AddSeconds(seconds));
            }
        }
        catch (JsonException)
        {
        }

        throw new HttpRequestException("The token endpoint answered without a usable access_token and expires_in.");
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
