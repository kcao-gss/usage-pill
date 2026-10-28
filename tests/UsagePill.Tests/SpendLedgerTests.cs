using UsagePill.Core;
using UsagePill.Pacing;

namespace UsagePill.Tests;

public class SpendLedgerTests
{
    private static readonly DateTimeOffset October = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset November = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static SpendInfo Spend(decimal used, string currency = "USD") => new(used, 1000m, currency, Severity.Normal);

    private static DateOnly Oct(int day) => new(2026, 10, day);

    [Fact]
    public void ADayKeepsItsFirstReadingAndFollowsTheLatest()
    {
        var ledger = SpendLedger.Empty
            .Record(Spend(100m), October, Oct(14))
            .Record(Spend(120m), October, Oct(14))
            .Record(Spend(130m), October, Oct(14));

        Assert.Equal(new SpendDayReading(100m, 130m), ledger.Days[Oct(14)]);
    }

    [Fact]
    public void ANewMonthStartsOver()
    {
        var ledger = SpendLedger.Empty
            .Record(Spend(900m), October, Oct(31))
            .Record(Spend(4m), November, Oct(31));

        Assert.Equal(November, ledger.MonthStart);
        Assert.Equal(new SpendDayReading(4m, 4m), Assert.Single(ledger.Days).Value);
    }

    [Fact]
    public void ATotalBelowOneAlreadySeenStartsOver()
    {
        var ledger = SpendLedger.Empty
            .Record(Spend(400m), October, Oct(13))
            .Record(Spend(50m), October, Oct(14));

        Assert.Equal(Oct(14), Assert.Single(ledger.Days).Key);
    }

    [Fact]
    public void ANewCurrencyStartsOver()
    {
        var ledger = SpendLedger.Empty
            .Record(Spend(400m), October, Oct(13))
            .Record(Spend(500m, "EUR"), October, Oct(14));

        Assert.Equal("EUR", ledger.Currency);
        Assert.Equal(Oct(14), Assert.Single(ledger.Days).Key);
    }

    [Fact]
    public void ADayOpensAtTheLatestTotalOfTheLastDaySeenBeforeIt()
    {
        var ledger = SpendLedger.Empty
            .Record(Spend(300m), October, Oct(9))
            .Record(Spend(380m), October, Oct(11))
            .Record(Spend(440m), October, Oct(14));

        // Oct 12 and 13 were never seen: the spend made then counts toward Oct 14.
        Assert.Equal(380m, ledger.OpeningOf(Oct(12), startsMonth: false));
        Assert.Equal(380m, ledger.OpeningOf(Oct(14), startsMonth: false));
        Assert.Equal(440m, ledger.OpeningOf(Oct(15), startsMonth: false));
    }

    [Fact]
    public void WithNothingSeenBeforeTheMonthStartOpensAtZero()
    {
        var ledger = SpendLedger.Empty.Record(Spend(12m), October, Oct(1));

        Assert.Equal(0m, ledger.OpeningOf(new DateOnly(2026, 9, 30), startsMonth: true));
    }

    [Fact]
    public void WithNothingSeenBeforeALaterDayItOpensAtTheFirstReadingAfter()
    {
        var ledger = SpendLedger.Empty.Record(Spend(150m), October, Oct(28)).Record(Spend(160m), October, Oct(28));

        Assert.Equal(150m, ledger.OpeningOf(Oct(26), startsMonth: false));
        Assert.Equal(150m, ledger.OpeningOf(Oct(28), startsMonth: false));
    }

    [Fact]
    public void AnEmptyLedgerKnowsNoOpeningExceptTheMonthStart()
    {
        Assert.Null(SpendLedger.Empty.OpeningOf(Oct(14), startsMonth: false));
        Assert.Equal(0m, SpendLedger.Empty.OpeningOf(Oct(1), startsMonth: true));
    }
}
