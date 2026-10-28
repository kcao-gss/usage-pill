using UsagePill.Pacing;

namespace UsagePill.Tests;

public class SpendMonthTests
{
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

    // Standard time UTC-3, with DST starting at local midnight on Oct 18, so 00:00 does not exist
    // that day. Some real zones have done this (Brazil until 2019).
    private static readonly TimeZoneInfo MidnightDst = TimeZoneInfo.CreateCustomTimeZone(
        "Midnight DST", TimeSpan.FromHours(-3), "Midnight DST", "Standard", "Daylight",
        new[]
        {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 10, 18),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 2, 20)),
        });

    private static DateTimeOffset Cdt(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void TheMonthIsTheUtcCalendarMonth()
    {
        var month = SpendMonth.Containing(Cdt(10, 28, 14, 8), Central);

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), month.Span.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), month.Span.End);
        Assert.Equal("October", month.Name);
    }

    [Fact]
    public void AnInstantAfterTheUtcMonthEndBelongsToTheNextMonthEvenOnTheSameLocalDate()
    {
        // Oct 31, 8 PM CDT is Nov 1, 01:00 UTC.
        var month = SpendMonth.Containing(Cdt(10, 31, 20), Central);

        Assert.Equal("November", month.Name);
        Assert.Equal(new DateOnly(2026, 10, 31), month.Days[0].Date);
    }

    [Fact]
    public void CentralOctoberStartsWithAnEveningAndEndsAt7Pm()
    {
        var month = SpendMonth.Containing(Cdt(10, 28, 14, 8), Central);

        Assert.Equal(32, month.Days.Count);
        Assert.Equal(new DateOnly(2026, 9, 30), month.Days[0].Date);
        Assert.Equal(Cdt(9, 30, 19), month.Days[0].Span.Start);
        Assert.Equal(Cdt(10, 1, 0), month.Days[0].Span.End);
        Assert.Equal(new DateOnly(2026, 10, 31), month.Days[^1].Date);
        Assert.Equal(Cdt(10, 31, 0), month.Days[^1].Span.Start);
        Assert.Equal(Cdt(10, 31, 19), month.Days[^1].Span.End);
    }

    [Fact]
    public void TheDayDstEndsLasts25Hours()
    {
        var month = SpendMonth.Containing(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero), Central);
        var sunday = month.Days[1];

        Assert.Equal(new DateOnly(2026, 11, 1), sunday.Date);
        Assert.Equal(TimeSpan.FromHours(25), sunday.Span.End - sunday.Span.Start);
    }

    [Fact]
    public void EastOfUtcTheMonthEndsOnTheMorningOfTheFirst()
    {
        var month = SpendMonth.Containing(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero), Tokyo);

        Assert.Equal(31, month.Days.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), month.Days[0].Date);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(9)), month.Days[0].Span.Start);
        Assert.Equal(new DateOnly(2026, 12, 1), month.Days[^1].Date);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.FromHours(9)), month.Days[^1].Span.End);
    }

    [Fact]
    public void ADayWhoseMidnightIsSkippedStartsAtTheFirstValidMinute()
    {
        var month = SpendMonth.Containing(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero), MidnightDst);
        var day = month.Days.Single(d => d.Date == new DateOnly(2026, 10, 18));

        // 00:00 standard (UTC-3) jumps to 01:00 daylight (UTC-2): both are 03:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 3, 0, 0, TimeSpan.Zero), day.Span.Start);
    }

    public static TheoryData<string, int> ZonesAndMonths()
    {
        var data = new TheoryData<string, int>();
        foreach (var zone in new[] { "UTC", "Central Standard Time", "Tokyo Standard Time", "Midnight DST" })
        {
            foreach (var month in new[] { 2, 3, 10, 11 }) data.Add(zone, month);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ZonesAndMonths))]
    public void TheDaysTileTheMonthWithoutGapsOrOverlaps(string zoneId, int monthNumber)
    {
        var zone = zoneId == "Midnight DST" ? MidnightDst : TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var month = SpendMonth.Containing(new DateTimeOffset(2026, monthNumber, 15, 0, 0, 0, TimeSpan.Zero), zone);

        Assert.Equal(month.Span.Start, month.Days[0].Span.Start);
        Assert.Equal(month.Span.End, month.Days[^1].Span.End);
        for (var i = 1; i < month.Days.Count; i++)
        {
            Assert.Equal(month.Days[i - 1].Span.End, month.Days[i].Span.Start);
            Assert.Equal(month.Days[i - 1].Date.AddDays(1), month.Days[i].Date);
        }
    }

    [Fact]
    public void IndexOfFindsTheLocalDaySoDaysLeftCountsTodayToTheLastDay()
    {
        var at = Cdt(10, 28, 14, 8);
        var month = SpendMonth.Containing(at, Central);
        var today = month.IndexOf(at);

        Assert.Equal(new DateOnly(2026, 10, 28), month.Days[today].Date);
        Assert.Equal(4, month.Days.Count - today);
    }

    [Theory]
    [InlineData(14, 12, 18)] // Wed Oct 14: Mon Oct 12 to Sun Oct 18
    [InlineData(0, 0, 4)]    // Wed Sep 30 evening: the week is clipped to the month start
    [InlineData(31, 26, 31)] // Sat Oct 31: the week is clipped to the month end
    public void WeeksRunMondayToSundayClippedToTheMonth(int index, int first, int last)
    {
        var month = SpendMonth.Containing(Cdt(10, 28, 14, 8), Central);

        Assert.Equal((first, last), month.WeekOf(index));
    }
}
