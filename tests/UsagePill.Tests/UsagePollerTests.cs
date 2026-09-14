using System.Net.Http;
using Microsoft.Extensions.Time.Testing;
using UsagePill.Core;
using UsagePill.Polling;

namespace UsagePill.Tests;

public class UsagePollerTests
{
    private sealed class StubProvider : IUsageProvider
    {
        private readonly Queue<Func<UsageSnapshot>> _responses = new();

        public string ProviderId => "stub";
        public string DisplayName => "Stub";
        public int Calls { get; private set; }

        public void EnqueueSuccess(double percent) => _responses.Enqueue(() => new UsageSnapshot(
            new[] { new UsageLimit(LimitKind.Session, percent, Severity.Normal, null, null) },
            null,
            DateTimeOffset.UnixEpoch));

        public void EnqueueFailure(Exception error) => _responses.Enqueue(() => throw error);

        public Task<UsageSnapshot> FetchAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_responses.Dequeue()());
        }
    }

    /// <summary>A provider whose fetch does not complete until the test releases it, so a
    /// poll can be suspended mid-flight while a concurrent Dispose() races it.</summary>
    private sealed class BlockingProvider : IUsageProvider
    {
        private readonly TaskCompletionSource<UsageSnapshot> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ProviderId => "blocking";
        public string DisplayName => "Blocking";

        public void Release(UsageSnapshot snapshot) => _tcs.SetResult(snapshot);

        public Task<UsageSnapshot> FetchAsync(CancellationToken ct) => _tcs.Task;
    }

    private static (UsagePoller poller, StubProvider provider, FakeTimeProvider clock) Build()
    {
        var provider = new StubProvider();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(provider, new BackoffPolicy(TimeSpan.FromMinutes(5)), clock);
        return (poller, provider, clock);
    }

    [Fact]
    public void StartsInLoading()
    {
        var (poller, _, _) = Build();

        Assert.Equal(UsageStatus.Loading, poller.State.Status);
    }

    [Fact]
    public async Task SuccessPublishesOk()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueSuccess(42);

        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.Ok, poller.State.Status);
        Assert.Equal(42, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public async Task FailureAfterSuccessKeepsTheOldSnapshotAndMarksStale()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueSuccess(42);
        provider.EnqueueFailure(new RateLimitedException(TimeSpan.FromMinutes(3)));

        await poller.RefreshNowAsync();
        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.Stale, poller.State.Status);
        Assert.Equal(42, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 12, 3, 0, TimeSpan.Zero), poller.State.RetryAt);
    }

    [Fact]
    public async Task MissingCredentialsPublishNoCredentials()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueFailure(new NoCredentialsException("nope"));

        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.NoCredentials, poller.State.Status);
    }

    [Fact]
    public async Task ExpiredTokenPublishesAuthExpired()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueFailure(new AuthExpiredException("nope"));

        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.AuthExpired, poller.State.Status);
    }

    [Fact]
    public async Task EveryPublishRaisesStateChanged()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueSuccess(1);
        provider.EnqueueFailure(new HttpRequestException("boom"));
        var seen = new List<UsageStatus>();
        poller.StateChanged += (_, state) => seen.Add(state.Status);

        await poller.RefreshNowAsync();
        await poller.RefreshNowAsync();

        Assert.Equal(new[] { UsageStatus.Ok, UsageStatus.Stale }, seen);
    }

    [Fact]
    public async Task TheTimerPollsAgainAfterTheBackoffDelay()
    {
        var (poller, provider, clock) = Build();
        provider.EnqueueSuccess(1);
        provider.EnqueueSuccess(2);
        poller.Start();
        await poller.WaitForIdleAsync();

        clock.Advance(TimeSpan.FromMinutes(5));
        await poller.WaitForIdleAsync();

        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public async Task ForeignCancellationPublishesFailureAndReschedulesTheTimer()
    {
        var (poller, provider, clock) = Build();
        provider.EnqueueFailure(new TaskCanceledException("http request timed out"));
        provider.EnqueueSuccess(9);

        poller.Start();
        await poller.WaitForIdleAsync();

        Assert.Equal(UsageStatus.Stale, poller.State.Status);

        // A TaskCanceledException the poller never requested (our own token was never
        // cancelled) must be treated as an ordinary transient failure: the ladder's first
        // step is one minute, and the loop must still reschedule itself.
        clock.Advance(TimeSpan.FromMinutes(1));
        await poller.WaitForIdleAsync();

        Assert.Equal(UsageStatus.Ok, poller.State.Status);
        Assert.Equal(9, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public async Task ThrowingSubscriberDoesNotBreakTheLoopOrCorruptTheBackoffLadder()
    {
        var (poller, provider, clock) = Build();
        provider.EnqueueSuccess(1);
        provider.EnqueueSuccess(2);
        poller.StateChanged += (_, _) => throw new InvalidOperationException("subscriber boom");

        poller.Start();
        await poller.WaitForIdleAsync();

        Assert.Equal(UsageStatus.Ok, poller.State.Status);
        Assert.Equal(1, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);

        // A throwing subscriber must not be mistaken for a provider failure: the next
        // poll is still due after the normal five minute interval, not a shorter,
        // wrongly-advanced backoff step.
        clock.Advance(TimeSpan.FromMinutes(5));
        await poller.WaitForIdleAsync();

        Assert.Equal(UsageStatus.Ok, poller.State.Status);
        Assert.Equal(2, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public async Task AuthExpiredAfterSuccessKeepsTheOldSnapshot()
    {
        var (poller, provider, _) = Build();
        provider.EnqueueSuccess(55);
        provider.EnqueueFailure(new AuthExpiredException("token expired"));

        await poller.RefreshNowAsync();
        await poller.RefreshNowAsync();

        Assert.Equal(UsageStatus.AuthExpired, poller.State.Status);
        Assert.Equal(55, poller.State.Snapshot!.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public async Task DisposeDuringAnInFlightPollDoesNotThrow()
    {
        var provider = new BlockingProvider();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(provider, new BackoffPolicy(TimeSpan.FromMinutes(5)), clock);
        var seen = new List<UsageStatus>();
        poller.StateChanged += (_, state) => seen.Add(state.Status);

        var refresh = poller.RefreshNowAsync();
        poller.Dispose();
        provider.Release(new UsageSnapshot(
            new[] { new UsageLimit(LimitKind.Session, 1, Severity.Normal, null, null) },
            null,
            DateTimeOffset.UnixEpoch));

        await refresh;

        // A disposed poller must never publish a result it was mid-flight on: state and
        // subscribers stay exactly as they were before Dispose() ran.
        Assert.Equal(UsageStatus.Loading, poller.State.Status);
        Assert.Empty(seen);
    }
}
