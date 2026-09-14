namespace UsagePill.Core;

public sealed class NoCredentialsException : Exception
{
    public NoCredentialsException(string message) : base(message) { }
}

public sealed class AuthExpiredException : Exception
{
    public AuthExpiredException(string message) : base(message) { }
}

public sealed class RateLimitedException : Exception
{
    public RateLimitedException(TimeSpan? retryAfter)
        : base("The usage endpoint rate limited the request.")
        => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}
