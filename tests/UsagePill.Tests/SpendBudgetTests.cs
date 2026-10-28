using UsagePill.Core;
using UsagePill.Pacing;

namespace UsagePill.Tests;

public class SpendBudgetTests
{
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    private static DateTimeOffset Cdt(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.FromHours(-5));

    private static SpendInfo Spend(decimal used, decimal limit = 1000m) => new(used, limit, "USD", Severity.Normal);

    /// <summary>Feeds the readings into a ledger the way SpendTracker does, then computes the budget at the last one.</summary>
    private static SpendBudget Budget(decimal limit, params (DateTimeOffset At, decimal Used)[] readings)
    {
        var ledger = SpendLedger.Empty;
        SpendMonth month = null!;
        var today = 0;
        foreach (var (at, used) in readings)
        {
            month = SpendMonth.Containing(at, Central);
            today = month.IndexOf(at);
            ledger = ledger.Record(Spend(used, limit), month.Span.Start, month.Days[today].Date);
        }
        return SpendBudget.Compute(Spend(readings[^1].Used, limit), ledger, month, today);
    }

    [Fact]
    public void TheSpecExampleWednesdayOctober14()
    {
        // $380 spent by Monday Oct 12, $430 by midnight, $470 now (spec section 4).
        var budget = Budget(1000m, (Cdt(10, 11, 22), 380m), (Cdt(10, 13, 22), 430m), (Cdt(10, 14, 9), 440m), (Cdt(10, 14, 14), 470m));

        Assert.Equal(40m, budget.Today.Spent);
        Assert.Equal(31.67m, Math.Round(budget.Today.Share, 2));
        Assert.Equal(126, (int)Math.Floor(budget.Today.Percent));
        Assert.Equal(90m, budget.Week.Spent);
        Assert.Equal(217m, budget.Week.Share);
        Assert.Equal(41, (int)Math.Floor(budget.Week.Percent));
        Assert.Equal("October", budget.MonthName);
        Assert.Equal(Cdt(10, 31, 19), budget.ResetsAt);
    }

    [Fact]
    public void AQuietDayRaisesTheNextDaysShare()
    {
        var busy = Budget(1000m, (Cdt(10, 13, 22), 430m), (Cdt(10, 14, 22), 470m), (Cdt(10, 15, 9), 470m));
        var quiet = Budget(1000m, (Cdt(10, 13, 22), 430m), (Cdt(10, 14, 22), 430m), (Cdt(10, 15, 9), 430m));

        Assert.True(quiet.Today.Share > busy.Today.Share);
        Assert.Equal(570m / 17, quiet.Today.Share);
    }

    [Fact]
    public void ACapChangeReSpreadsWhatIsLeftAtTheNewCap()
    {
        // Oct 28 2026: $93 used under a $100 cap by the time the day began, then the cap became $1,000.
        var budget = Budget(1000m, (Cdt(10, 27, 22), 93m), (Cdt(10, 28, 14), 150m));

        Assert.Equal((1000m - 93m) / 4, budget.Today.Share);
        Assert.Equal(57m, budget.Today.Spent);
    }

    [Fact]
    public void WithTheCapGoneTheShareReadsAsFull()
    {
        var budget = Budget(100m, (Cdt(10, 27, 22), 100m), (Cdt(10, 28, 14), 100m));

        Assert.Equal(0m, budget.Today.Share);
        Assert.Equal(100, budget.Today.Percent);
    }

    [Fact]
    public void TheFirstDayAfterInstallingStartsTodayAndThisWeekAtZero()
    {
        var budget = Budget(1000m, (Cdt(10, 28, 14), 150.03m));

        Assert.Equal(0m, budget.Today.Spent);
        Assert.Equal(0m, budget.Week.Spent);
        Assert.Equal(0, budget.Today.Percent);
    }

    [Fact]
    public void TheMonthsFirstEveningOpensAtZero()
    {
        // Sep 30, 9 PM CDT is already October in UTC.
        var budget = Budget(1000m, (Cdt(9, 30, 21), 12m));

        Assert.Equal(12m, budget.Today.Spent);
        Assert.Equal(1000m / 32, budget.Today.Share);
    }

    [Fact]
    public void BarsMarkTodayTheFutureUnseenDaysAndOverspend()
    {
        var budget = Budget(1000m, (Cdt(10, 11, 22), 380m), (Cdt(10, 13, 22), 430m), (Cdt(10, 14, 14), 470m));
        var bars = budget.Days;

        Assert.Equal(32, bars.Count);
        Assert.Null(bars[12].Spent);                // Oct 12 was never seen
        Assert.Equal(50m, bars[13].Spent);          // Oct 13 carries Oct 12 too
        Assert.True(bars[14].IsToday);
        Assert.True(bars[14].Over);                 // $40 against $31.67
        Assert.True(bars[15].IsFuture);
        Assert.Null(bars[15].Spent);
        Assert.Equal(1000m / 32, budget.EvenSplit);
    }

    [Fact]
    public void FromTheMonthsFirstDayTheBarsAddUpToAllTheSpend()
    {
        var budget = Budget(1000m, (Cdt(9, 30, 21), 12m), (Cdt(10, 3, 12), 90m), (Cdt(10, 9, 12), 300m), (Cdt(10, 14, 14), 470m));

        Assert.Equal(470m, budget.Days.Sum(d => d.Spent ?? 0));
    }

    [Fact]
    public void FromAMidMonthStartTheBarsAddUpToTheSpendSinceTheFirstReading()
    {
        var budget = Budget(1000m, (Cdt(10, 9, 12), 300m), (Cdt(10, 12, 12), 380m), (Cdt(10, 14, 14), 470m));

        Assert.Equal(470m - 300m, budget.Days.Sum(d => d.Spent ?? 0));
    }
}
