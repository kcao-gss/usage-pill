namespace UsagePill.Claude;

/// <summary>
/// The Claude Code OAuth credentials. <see cref="ToString"/> is overridden to redact
/// <see cref="AccessToken"/>, so interpolating an instance NEVER leaks the token.
/// <see cref="ExpiresAt"/> is null when the credentials file carries no usable
/// expiry, which ranks those credentials oldest when several sources compete.
/// </summary>
public sealed record ClaudeCredentials(string AccessToken, DateTimeOffset? ExpiresAt)
{
    public override string ToString() => $"ClaudeCredentials {{ AccessToken = (redacted), ExpiresAt = {ExpiresAt:o} }}";
}
