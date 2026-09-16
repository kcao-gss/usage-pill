using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>
/// Chooses one credentials file out of all the places Claude Code can be signed in,
/// Windows and WSL alike, and keeps using it until it stops working.
///
/// The freshest token wins: the file whose claudeAiOauth.expiresAt is latest belongs to
/// the Claude Code that signed in or refreshed most recently. A file with no expiry ranks
/// oldest, so it is only used when nothing better exists.
///
/// Steady state costs one file read per poll, because Claude Code refreshes the token in
/// place. A full scan runs only at startup, when the chosen file stops being readable, and
/// when <see cref="Invalidate"/> reports that the endpoint rejected the token.
///
/// A token the endpoint has rejected ranks below every other readable token, so the next
/// scan hands over to a second Claude Code instead of choosing the same dead token again.
/// </summary>
public sealed class ClaudeCredentialResolver : IClaudeCredentialSource
{
    private readonly Func<IReadOnlyList<string>> _candidatePaths;
    private readonly Func<string, ClaudeCredentials> _readPath;
    private readonly object _gate = new();

    private string? _chosenPath;
    private string? _servedToken;
    private string? _rejectedToken;

    public ClaudeCredentialResolver(Func<IReadOnlyList<string>> candidatePaths)
        : this(candidatePaths, path => new ClaudeCredentialStore(path).Read())
    {
    }

    public ClaudeCredentialResolver(Func<IReadOnlyList<string>> candidatePaths, Func<string, ClaudeCredentials> readPath)
    {
        _candidatePaths = candidatePaths;
        _readPath = readPath;
    }

    public static ClaudeCredentialResolver Default() => new(CredentialSources.All);

    public ClaudeCredentials Read()
    {
        lock (_gate)
        {
            if (_chosenPath is { } path)
            {
                try
                {
                    return Serve(_readPath(path));
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

    private ClaudeCredentials Serve(ClaudeCredentials credentials)
    {
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
                candidate = _readPath(path);
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
    /// Orders the candidates: a token the endpoint has not rejected beats the rejected one,
    /// and the latest expiry wins among the rest. A file with no expiry ranks oldest.
    /// </summary>
    private (bool NotRejected, DateTimeOffset ExpiresAt) Rank(ClaudeCredentials credentials) =>
        (credentials.AccessToken != _rejectedToken, credentials.ExpiresAt ?? DateTimeOffset.MinValue);
}
