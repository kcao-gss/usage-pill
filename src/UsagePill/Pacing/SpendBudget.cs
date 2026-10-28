using UsagePill.Core;

namespace UsagePill.Pacing;

/// <summary>The spend in one period against that period's share of the cap.</summary>
public sealed record SpendPace(decimal Spent, decimal Share)
{
    /// <summary>Not capped, so being 26% over reads as 126. A share of zero or less (the cap is used up) reads as 100.</summary>
    public double Percent => Share > 0 ? (double)(Spent / Share * 100) : 100;

    /// <summary>What is left of the share. Negative once over.</summary>
    public decimal Left => Share - Spent;
}

/// <summary>One bar of the detail card's daily strip. <see cref="Spent"/> is null on days the app never saw and on future days.</summary>
public sealed record SpendDayBar(DateOnly Date, decimal? Spent, decimal Share, bool IsToday, bool IsFuture)
{
    public bool Over => Spent > Share;
}

/// <summary>
/// The month's cap split into roll-forward shares (spec section 4). What was left when a period
/// began is spread evenly over the days left then, so a quiet day raises every later share.
/// </summary>
public sealed record SpendBudget(
    SpendInfo Spend,
    SpendPace Today,
    SpendPace Week,
    string MonthName,
    DateTimeOffset ResetsAt,
    decimal EvenSplit,
    IReadOnlyList<SpendDayBar> Days)
{
    /// <summary><paramref name="ledger"/> must already hold <paramref name="spend"/>, recorded on day <paramref name="today"/>.</summary>
    public static SpendBudget Compute(SpendInfo spend, SpendLedger ledger, SpendMonth month, int today)
    {
        var bars = new SpendDayBar[month.Days.Count];
        for (var i = 0; i < bars.Length; i++)
        {
            var date = month.Days[i].Date;
            if (i > today)
            {
                bars[i] = new SpendDayBar(date, null, 0, IsToday: false, IsFuture: true);
                continue;
            }

            var opening = Opening(spend, ledger, month, i);
            decimal? spent = ledger.Days.TryGetValue(date, out var seen) ? seen.Last - opening : null;
            bars[i] = new SpendDayBar(date, spent, Share(spend, month, i, opening, 1), IsToday: i == today, IsFuture: false);
        }

        var (weekFirst, weekLast) = month.WeekOf(today);
        return new SpendBudget(
            spend,
            Pace(spend, ledger, month, today, 1),
            Pace(spend, ledger, month, weekFirst, weekLast - weekFirst + 1),
            month.Name,
            month.Span.End,
            spend.Limit / month.Days.Count,
            bars);
    }

    private static SpendPace Pace(SpendInfo spend, SpendLedger ledger, SpendMonth month, int first, int length)
    {
        var opening = Opening(spend, ledger, month, first);
        return new SpendPace(spend.Used - opening, Share(spend, month, first, opening, length));
    }

    /// <summary>What was left when day <paramref name="first"/> began, spread over the days left then, for <paramref name="length"/> days.</summary>
    private static decimal Share(SpendInfo spend, SpendMonth month, int first, decimal opening, int length) =>
        (spend.Limit - opening) / (month.Days.Count - first) * length;

    private static decimal Opening(SpendInfo spend, SpendLedger ledger, SpendMonth month, int day) =>
        ledger.OpeningOf(month.Days[day].Date, startsMonth: day == 0) ?? spend.Used;
}
