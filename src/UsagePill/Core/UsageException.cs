namespace UsagePill.Core;

public sealed class NoCredentialsException : Exception
{
    public NoCredentialsException(string message) : base(message) { }
}

public sealed class AuthExpiredException : Exception
{
    public AuthExpiredException(string message) : base(message) { }
}

/// <summary>
/// The token lacks the user:profile scope the usage endpoint requires. Unlike an expired
/// token, no refresh fixes this: Claude Code has to sign in again.
/// </summary>
public sealed class MissingScopeException : Exception
{
    public MissingScopeException(string message) : base(message) { }
}

public sealed class RateLimitedException : Exception
{
    public RateLimitedException(TimeSpan? retryAfter)
        : base("The usage endpoint rate limited the request.")
        => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}
