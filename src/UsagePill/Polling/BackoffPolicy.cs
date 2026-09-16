using UsagePill.Core;

namespace UsagePill.Polling;

/// <summary>
/// Decides how long to wait before the next poll. The usage endpoint rate limits
/// aggressively, so the 429 ladder is much longer than the transient one. The auth ladder
/// runs the other way: it starts within seconds and never exceeds the normal interval.
/// </summary>
public sealed class BackoffPolicy
{
    private static readonly TimeSpan RateLimitFirst = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RateLimitCap = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan TransientFirst = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TransientCap = TimeSpan.FromMinutes(15);

    // Claude Code refreshes the token in place, usually within a minute of the pill seeing it
    // rejected. Retrying that soon turns a stale "login expired" into live numbers, and the
    // ladder climbs to the normal interval so a signed-out machine costs no extra requests.
    private static readonly TimeSpan AuthFirst = TimeSpan.FromSeconds(15);

    // The endpoint can answer "Retry-After: 0", and a past HTTP-date clamps to zero.
    // Backing off for zero seconds would spin against the server that just rate limited us.
    private static readonly TimeSpan RetryAfterFloor = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _normalInterval;
    private int _rateLimitStep;
    private int _transientStep;
    private int _authStep;

    public BackoffPolicy(TimeSpan normalInterval) => _normalInterval = normalInterval;

    public TimeSpan NextDelay(Exception? failure)
    {
        switch (failure)
        {
            case null:
                Reset();
                return _normalInterval;

            case AuthExpiredException:
            case NoCredentialsException:
                return Climb(AuthFirst, _normalInterval, ref _authStep);

            case RateLimitedException rateLimited:
                if (rateLimited.RetryAfter is { } retryAfter)
                {
                    return retryAfter > RetryAfterFloor ? retryAfter : RetryAfterFloor;
                }
                return Climb(RateLimitFirst, RateLimitCap, ref _rateLimitStep);

            default:
                return Climb(TransientFirst, TransientCap, ref _transientStep);
        }
    }

    public void Reset()
    {
        _rateLimitStep = 0;
        _transientStep = 0;
        _authStep = 0;
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
