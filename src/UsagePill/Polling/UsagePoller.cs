using UsagePill.Core;

namespace UsagePill.Polling;

/// <summary>
/// Polls one provider on a timer and publishes the resulting state. A failed poll
/// never stops the loop and never loses the last good snapshot.
/// </summary>
public sealed class UsagePoller : IDisposable
{
    private readonly IUsageProvider _provider;
    private readonly BackoffPolicy _backoff;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ITimer? _timer;
    private volatile Task _inFlight = Task.CompletedTask;
    private volatile bool _disposed;

    public UsagePoller(IUsageProvider provider, BackoffPolicy backoff, TimeProvider clock)
    {
        _provider = provider;
        _backoff = backoff;
        _clock = clock;
    }

    public UsageState State { get; private set; } = UsageState.Loading;

    public event EventHandler<UsageState>? StateChanged;

    public void Start()
    {
        if (_disposed || _timer is not null) return;

        // Create the timer disarmed first, so the field is assigned before the first
        // callback can run: the fake clock used in tests invokes a due-zero callback
        // synchronously, and RefreshNowAsync reads _timer to reschedule itself.
        _timer = _clock.CreateTimer(
            _ => _inFlight = RefreshNowAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Waits for the poll that the timer started, so tests are deterministic.</summary>
    public Task WaitForIdleAsync() => _inFlight;

    public async Task RefreshNowAsync()
    {
        if (_disposed) return;

        // _cts.Token throws ObjectDisposedException once Dispose() has run; read it once
        // up front and reuse it for both waits below, instead of risking a second, later
        // access racing a concurrent Dispose().
        CancellationToken token;
        try
        {
            token = _cts.Token;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed) return;

            Exception? failure = null;
            try
            {
                var snapshot = await _provider.FetchAsync(token).ConfigureAwait(false);
                Publish(new UsageState(UsageStatus.Ok, snapshot, null, null));
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // Only our own teardown cancels this token. A foreign OperationCanceledException
                // (for example HttpClient.Timeout expiring the caller's uncancelled token) falls
                // through to the general handler below and is treated as an ordinary failure, so
                // one HTTP timeout can never stop the poll loop.
                return;
            }
            catch (Exception e)
            {
                failure = e;
            }

            var delay = _backoff.NextDelay(failure);
            if (failure is not null) Publish(BuildFailureState(failure, _clock.GetUtcNow() + delay));

            // A concurrent Dispose() may have torn the timer down while this poll was
            // in flight; rescheduling it then would throw ObjectDisposedException.
            if (!_disposed)
            {
                try
                {
                    _timer?.Change(delay, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
        finally
        {
            // Same race as above: Dispose() may have disposed the gate while this poll
            // held it, so releasing it here can no longer be assumed safe.
            if (!_disposed)
            {
                try
                {
                    _gate.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private UsageState BuildFailureState(Exception failure, DateTimeOffset retryAt) => failure switch
    {
        NoCredentialsException => new UsageState(UsageStatus.NoCredentials, State.Snapshot, failure.Message, retryAt),
        AuthExpiredException => new UsageState(UsageStatus.AuthExpired, State.Snapshot, failure.Message, retryAt),
        _ => new UsageState(UsageStatus.Stale, State.Snapshot, failure.Message, retryAt),
    };

    private void Publish(UsageState state)
    {
        // Dispose() may run while a suspended poll is resuming; a disposed poller must
        // never mutate State or notify a subscriber that has already been torn down.
        if (_disposed) return;

        State = state;
        try
        {
            StateChanged?.Invoke(this, state);
        }
        catch (Exception)
        {
            // A misbehaving subscriber must never be misattributed to the provider (that
            // would corrupt the backoff ladder on an otherwise successful poll) or break
            // the poll loop (the timer callback is fire-and-forget, so an unhandled
            // exception here would silently stop polling forever).
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _timer?.Dispose();
        _cts.Dispose();
        _gate.Dispose();
    }
}
