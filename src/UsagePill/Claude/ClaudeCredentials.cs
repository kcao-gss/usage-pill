namespace UsagePill.Claude;

/// <summary>
/// The Claude Code OAuth credentials. <see cref="ToString"/> is overridden to redact
/// <see cref="AccessToken"/>, so interpolating an instance NEVER leaks the token.
/// <see cref="ExpiresAt"/> is null when the credentials file carries no usable
/// expiry, which ranks those credentials oldest when several sources compete.
/// <see cref="HasProfileScope"/> is false only when the file lists its scopes and
/// <see cref="ProfileScope"/> is not among them; the usage endpoint refuses such a token.
/// </summary>
public sealed record ClaudeCredentials(string AccessToken, DateTimeOffset? ExpiresAt, bool HasProfileScope = true)
{
    public const string ProfileScope = "user:profile";

    public override string ToString() =>
        $"ClaudeCredentials {{ AccessToken = (redacted), ExpiresAt = {ExpiresAt:o}, HasProfileScope = {HasProfileScope} }}";
}
