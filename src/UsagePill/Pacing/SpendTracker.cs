using System.IO;
using UsagePill.Core;

namespace UsagePill.Pacing;

/// <summary>
/// Keeps the spend ledger current and turns it into a budget. A snapshot is recorded once, and
/// the budget is computed at its capture time, so stale data shows the budget it had when it was
/// fresh instead of mixing an old total with a new day.
/// </summary>
public sealed class SpendTracker
{
    private readonly SpendLedgerStore _store;
    private readonly TimeZoneInfo _zone;
    private SpendLedger _ledger;
    private DateTimeOffset? _recordedAt;
    private SpendBudget? _budget;

    public SpendTracker(SpendLedgerStore store, TimeZoneInfo zone)
    {
        _store = store;
        _zone = zone;
        _ledger = store.Load();
    }

    /// <summary>The budget for the state's last good snapshot, or null when the account is not spend-only.</summary>
    public SpendBudget? Update(UsageState state)
    {
        if (state.Snapshot is not { IsSpendOnly: true, Spend: { } spend } snapshot) return null;
        if (snapshot.CapturedAt == _recordedAt) return _budget;

        var month = SpendMonth.Containing(snapshot.CapturedAt, _zone);
        var today = month.IndexOf(snapshot.CapturedAt);
        _ledger = _ledger.Record(spend, month.Span.Start, month.Days[today].Date);
        _recordedAt = snapshot.CapturedAt;
        _budget = SpendBudget.Compute(spend, _ledger, month, today);

        try
        {
            _store.Save(_ledger);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort, like settings: the rings still show this run's budget, and the next
            // poll retries the save.
        }

        return _budget;
    }
}
