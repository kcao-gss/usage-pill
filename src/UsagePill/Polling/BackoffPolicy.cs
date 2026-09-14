using UsagePill.Core;

namespace UsagePill.Polling;

/// <summary>
/// Decides how long to wait before the next poll. The usage endpoint rate limits
/// aggressively, so the 429 ladder is much longer than the transient one.
/// </summary>
public sealed class BackoffPolicy
{
    private static readonly TimeSpan RateLimitFirst = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RateLimitCap = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan TransientFirst = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TransientCap = TimeSpan.FromMinutes(15);

    private readonly TimeSpan _normalInterval;
    private int _rateLimitStep;
    private int _transientStep;

    public BackoffPolicy(TimeSpan normalInterval) => _normalInterval = normalInterval;

    public TimeSpan NextDelay(Exception? failure)
    {
        switch (failure)
        {
            case null:
            case AuthExpiredException:
            case NoCredentialsException:
                Reset();
                return _normalInterval;

            case RateLimitedException rateLimited:
                _transientStep = 0;
                if (rateLimited.RetryAfter is { } retryAfter) return retryAfter;
                return Climb(RateLimitFirst, RateLimitCap, ref _rateLimitStep);

            default:
                _rateLimitStep = 0;
                return Climb(TransientFirst, TransientCap, ref _transientStep);
        }
    }

    public void Reset()
    {
        _rateLimitStep = 0;
        _transientStep = 0;
    }

    private static TimeSpan Climb(TimeSpan first, TimeSpan cap, ref int step)
    {
        var doublings = step;
        step++;

        var ticks = first.Ticks;
        for (var i = 0; i < doublings && ticks < cap.Ticks; i++) ticks *= 2;

        return ticks >= cap.Ticks ? cap : TimeSpan.FromTicks(ticks);
    }
}
