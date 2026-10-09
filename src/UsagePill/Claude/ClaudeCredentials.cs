namespace UsagePill.Claude;

/// <summary>
/// The Claude Code OAuth credentials. <see cref="ToString"/> is overridden to redact
/// <see cref="AccessToken"/> and <see cref="RefreshToken"/>, so interpolating an instance
/// NEVER leaks a token. <see cref="ExpiresAt"/> is null when the credentials file carries no
/// usable expiry, which ranks those credentials oldest when several sources compete.
/// <see cref="HasProfileScope"/> is false only when the file lists its scopes and
/// <see cref="ProfileScope"/> is not among them; the usage endpoint refuses such a token.
/// <see cref="RefreshToken"/> is null when the file holds none, and then an expired access
/// token can only be renewed by Claude Code.
/// </summary>
public sealed record ClaudeCredentials(
    string AccessToken,
    DateTimeOffset? ExpiresAt,
    bool HasProfileScope = true,
    string? RefreshToken = null)
{
    public const string ProfileScope = "user:profile";

    public override string ToString() =>
        $"ClaudeCredentials {{ AccessToken = (redacted), ExpiresAt = {ExpiresAt:o}, HasProfileScope = {HasProfileScope}, " +
        $"RefreshToken = {(RefreshToken is null ? "(none)" : "(redacted)")} }}";
}

/// <summary>
/// A token pair the OAuth token endpoint issued in exchange for a refresh token.
/// <see cref="RefreshToken"/> is null when the endpoint kept the old refresh token valid.
/// </summary>
public sealed record ClaudeTokenGrant(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public override string ToString() =>
        $"ClaudeTokenGrant {{ AccessToken = (redacted), RefreshToken = {(RefreshToken is null ? "(none)" : "(redacted)")}, ExpiresAt = {ExpiresAt:o} }}";
}
