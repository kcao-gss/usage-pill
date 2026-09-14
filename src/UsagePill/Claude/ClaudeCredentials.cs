namespace UsagePill.Claude;

public sealed record ClaudeCredentials(string AccessToken, DateTimeOffset ExpiresAt, string? SubscriptionType);
