using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>
/// Chooses one credentials file out of all the places Claude Code can be signed in,
/// Windows and WSL alike, and keeps using it until it stops working.
///
/// The freshest live token wins: the file whose claudeAiOauth.expiresAt is latest belongs to
/// the Claude Code that signed in or refreshed most recently. A file with no expiry counts as
/// live but ranks oldest among the live ones, so it is only used when nothing better exists.
///
/// Steady state costs one file read per poll, because Claude Code refreshes the token in
/// place. A full scan runs only at startup, when the chosen file stops being readable, when
/// its token is past its own expiry, and when <see cref="Invalidate"/> reports that the
/// endpoint rejected the token or that it lacks the user:profile scope.
///
/// A token that is past its expiry or that the endpoint has rejected ranks below every live
/// token, so the next scan hands over to a second Claude Code instead of choosing a dead token
/// again. The expiry check cannot wait for a rejection: the usage endpoint can answer an
/// expired token with 429 instead of 401, and a 429 alone would pin the pill to that file.
///
/// A token without the user:profile scope ranks below even a dead one: a refresh renews a
/// dead token in place, but no refresh ever adds the scope the usage endpoint requires.
///
/// <see cref="RefreshAsync"/> renews an expired token in the file it was served from, and
/// in no other file.
/// </summary>
public sealed class ClaudeCredentialResolver : IClaudeCredentialSource
{
    private readonly Func<IReadOnlyList<string>> _candidatePaths;
    private readonly object _gate = new();
    private readonly TimeProvider _clock;

    private string? _chosenPath;
    private string? _servedPath;
    private string? _servedToken;
    private string? _rejectedToken;

    public ClaudeCredentialResolver(Func<IReadOnlyList<string>> candidatePaths, TimeProvider clock)
    {
        _candidatePaths = candidatePaths;
        _clock = clock;
    }

    public static ClaudeCredentialResolver Default() => new(CredentialSources.All, TimeProvider.System);

    public ClaudeCredentials Read()
    {
        lock (_gate)
        {
            if (_chosenPath is { } path)
            {
                try
                {
                    var credentials = new ClaudeCredentialStore(path).Read();
                    if (!IsExpired(credentials)) return Serve(credentials);

                    // Claude Code there stopped refreshing. Another one may be live.
                    _chosenPath = null;
                }
                catch (NoCredentialsException)
                {
                    // The distribution went away, or Claude Code signed out there.
                    _chosenPath = null;
                }
            }

            return Serve(Scan());
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            // Remember the token the endpoint rejected, not the file it came from: Claude Code
            // refreshes in place, so the same path holds a usable token again after a refresh.
            _rejectedToken = _servedToken;
            _chosenPath = null;
        }
    }

    public async Task<ClaudeCredentials> RefreshAsync(
        ClaudeCredentials current,
        Func<string, CancellationToken, Task<ClaudeTokenGrant>> exchange,
        CancellationToken ct)
    {
        string path;
        lock (_gate)
        {
            if (_servedPath is null || _servedToken != current.AccessToken)
            {
                throw new InvalidOperationException("Only the token served last can be refreshed.");
            }

            path = _servedPath;
        }

        await new ClaudeCredentialStore(path).RefreshAsync(current, exchange, ct).ConfigureAwait(false);
        return Read();
    }

    /// <summary>Every caller has just pointed <see cref="_chosenPath"/> at the file the token came from.</summary>
    private ClaudeCredentials Serve(ClaudeCredentials credentials)
    {
        _servedPath = _chosenPath;
        _servedToken = credentials.AccessToken;
        return credentials;
    }

    private ClaudeCredentials Scan()
    {
        ClaudeCredentials? best = null;
        string? bestPath = null;
        var examined = 0;

        foreach (var path in _candidatePaths())
        {
            examined++;

            ClaudeCredentials candidate;
            try
            {
                candidate = new ClaudeCredentialStore(path).Read();
            }
            catch (NoCredentialsException)
            {
                continue;
            }

            if (best is null || Rank(candidate).CompareTo(Rank(best)) > 0)
            {
                best = candidate;
                bestPath = path;
            }
        }

        if (best is null)
        {
            throw new NoCredentialsException($"No readable Claude credentials in {examined} checked location(s), Windows and WSL.");
        }

        _chosenPath = bestPath;
        return best;
    }

    /// <summary>
    /// Orders the candidates: a token carrying the profile scope beats every one without it.
    /// Among those, a live token, one neither past its expiry nor rejected by the endpoint,
    /// beats every dead one, and the latest expiry wins among equals. A file with no expiry
    /// counts as live and ranks oldest among the live ones.
    /// </summary>
    private (bool HasProfileScope, bool Live, DateTimeOffset ExpiresAt) Rank(ClaudeCredentials credentials) =>
        (credentials.HasProfileScope,
         credentials.AccessToken != _rejectedToken && !IsExpired(credentials),
         credentials.ExpiresAt ?? DateTimeOffset.MinValue);

    private bool IsExpired(ClaudeCredentials credentials) =>
        credentials.ExpiresAt is { } expiresAt && expiresAt <= _clock.GetUtcNow();
}
