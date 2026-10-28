using System.Collections.Immutable;
using UsagePill.Core;

namespace UsagePill.Pacing;

/// <summary>The first and the latest spend total read on one local day.</summary>
public sealed record SpendDayReading(decimal First, decimal Last);

/// <summary>
/// The app's own history of one budget month's spend, because the usage endpoint keeps none:
/// the first and latest total read on each local day. See spec section 5.
/// </summary>
public sealed record SpendLedger(
    DateTimeOffset MonthStart,
    string Currency,
    ImmutableSortedDictionary<DateOnly, SpendDayReading> Days)
{
    /// <summary>A ledger for no month at all. The first <see cref="Record"/> starts a real one.</summary>
    public static readonly SpendLedger Empty = new(DateTimeOffset.MinValue, "", ImmutableSortedDictionary<DateOnly, SpendDayReading>.Empty);

    /// <summary>Adds a reading of the month's spend taken on local day <paramref name="date"/>.</summary>
    public SpendLedger Record(SpendInfo spend, DateTimeOffset monthStart, DateOnly date)
    {
        var ledger = Continues(spend, monthStart)
            ? this
            : Empty with { MonthStart = monthStart, Currency = spend.Currency };
        var reading = ledger.Days.TryGetValue(date, out var seen)
            ? seen with { Last = spend.Used }
            : new SpendDayReading(spend.Used, spend.Used);
        return ledger with { Days = ledger.Days.SetItem(date, reading) };
    }

    /// <summary>
    /// The spend total when local day <paramref name="date"/> began: the latest total of the last
    /// day seen before it. With none, 0 if the day starts the month, and otherwise the first
    /// reading at or after it. Null when the ledger has nothing to go on.
    /// </summary>
    public decimal? OpeningOf(DateOnly date, bool startsMonth)
    {
        SpendDayReading? before = null;
        foreach (var (day, reading) in Days)
        {
            if (day >= date) return before?.Last ?? (startsMonth ? 0m : reading.First);
            before = reading;
        }
        return before?.Last ?? (startsMonth ? 0m : null);
    }

    /// <summary>A new month, a new currency, or a total below one already seen (an early reset, or another account) starts over.</summary>
    private bool Continues(SpendInfo spend, DateTimeOffset monthStart) =>
        MonthStart == monthStart
        && Currency == spend.Currency
        && (Days.IsEmpty || spend.Used >= Days.Values.Last().Last);
}
