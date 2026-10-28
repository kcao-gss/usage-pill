using System.Globalization;

namespace UsagePill.Pacing;

/// <summary>A half-open span of time, [Start, End).</summary>
public readonly record struct Period(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset at) => at >= Start && at < End;
}

/// <summary>One local calendar day, clipped to its budget month.</summary>
public readonly record struct SpendDay(DateOnly Date, Period Span);

/// <summary>
/// One budget month: the UTC calendar month the spend cap resets on, cut into the local
/// calendar days of a time zone. The first and last day are usually partial, because a UTC
/// month rarely starts or ends at local midnight. See spec section 3.
/// </summary>
public sealed class SpendMonth
{
    private SpendMonth(Period span, IReadOnlyList<SpendDay> days)
    {
        Span = span;
        Days = days;
    }

    public Period Span { get; }

    public IReadOnlyList<SpendDay> Days { get; }

    /// <summary>The English month name, matching the rest of the UI copy.</summary>
    public string Name => Span.Start.UtcDateTime.ToString("MMMM", CultureInfo.InvariantCulture);

    public static SpendMonth Containing(DateTimeOffset at, TimeZoneInfo zone)
    {
        var utc = at.UtcDateTime;
        var start = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var span = new Period(start, start.AddMonths(1));

        var days = new List<SpendDay>();
        for (var date = LocalDate(span.Start, zone); ; date = date.AddDays(1))
        {
            var dayStart = Later(LocalMidnight(date, zone), span.Start);
            if (dayStart >= span.End) break;
            days.Add(new SpendDay(date, new Period(dayStart, Earlier(LocalMidnight(date.AddDays(1), zone), span.End))));
        }

        return new SpendMonth(span, days);
    }

    /// <summary>The index of the day containing <paramref name="at"/>, which must lie in this month.</summary>
    public int IndexOf(DateTimeOffset at)
    {
        for (var i = 0; i < Days.Count; i++)
        {
            if (Days[i].Span.Contains(at)) return i;
        }
        throw new ArgumentOutOfRangeException(nameof(at), at, "The instant lies outside this month.");
    }

    /// <summary>The first and last index of the days that share a Monday-to-Sunday week with day <paramref name="index"/>.</summary>
    public (int First, int Last) WeekOf(int index)
    {
        var monday = Monday(Days[index].Date);
        var first = index;
        while (first > 0 && Monday(Days[first - 1].Date) == monday) first--;
        var last = index;
        while (last < Days.Count - 1 && Monday(Days[last + 1].Date) == monday) last++;
        return (first, last);
    }

    private static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    /// <summary>
    /// The instant <paramref name="date"/> begins in <paramref name="zone"/>. Where a DST jump
    /// skips midnight the day begins at the first valid minute. Where midnight happens twice it
    /// begins at the first one.
    /// </summary>
    private static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
