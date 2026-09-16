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
/// </summary>
public sealed class ClaudeCredentialResolver : IClaudeCredentialSource
{
    private readonly Func<IReadOnlyList<string>> _candidatePaths;
    private readonly Func<string, ClaudeCredentials> _readPath;
    private readonly object _gate = new();

    private string? _chosenPath;

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
                    return _readPath(path);
                }
                catch (NoCredentialsException)
                {
                    // The distribution went away, or Claude Code signed out there.
                    _chosenPath = null;
                }
            }

            return Scan();
        }
    }

    public void Invalidate()
    {
        lock (_gate) _chosenPath = null;
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

            if (best is null || Expiry(candidate) > Expiry(best))
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

    private static DateTimeOffset Expiry(ClaudeCredentials credentials) =>
        credentials.ExpiresAt ?? DateTimeOffset.MinValue;
}
