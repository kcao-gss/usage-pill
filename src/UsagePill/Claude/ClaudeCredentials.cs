namespace UsagePill.Claude;

/// <summary>
/// The Claude Code OAuth credentials. <see cref="ToString"/> is overridden to redact
/// <see cref="AccessToken"/>, so interpolating an instance NEVER leaks the token.
/// </summary>
public sealed record ClaudeCredentials(string AccessToken, DateTimeOffset ExpiresAt, string? SubscriptionType)
{
    public override string ToString() =>
        $"ClaudeCredentials {{ AccessToken = (redacted), ExpiresAt = {ExpiresAt:O}, SubscriptionType = {SubscriptionType} }}";
}
