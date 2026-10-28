using System.IO;
using UsagePill.Core;
using UsagePill.Pacing;

namespace UsagePill.Tests;

public class SpendLedgerStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("usagepill").FullName;
    private string Path_ => Path.Combine(_dir, "spend-ledger.json");

    private static readonly DateTimeOffset October = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTripsThroughDisk()
    {
        var ledger = SpendLedger.Empty
            .Record(new SpendInfo(93.12m, 100m, "USD", Severity.Normal), October, new DateOnly(2026, 10, 27))
            .Record(new SpendInfo(141.80m, 1000m, "USD", Severity.Normal), October, new DateOnly(2026, 10, 27))
            .Record(new SpendInfo(150.03m, 1000m, "USD", Severity.Normal), October, new DateOnly(2026, 10, 28));
        var store = new SpendLedgerStore(Path_);

        store.Save(ledger);
        var loaded = store.Load();

        Assert.Equal(October, loaded.MonthStart);
        Assert.Equal("USD", loaded.Currency);
        Assert.Equal(ledger.Days, loaded.Days);
    }

    [Fact]
    public void AMissingFileIsAnEmptyLedger()
    {
        Assert.Same(SpendLedger.Empty, new SpendLedgerStore(Path_).Load());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "month": "October", "currency": "USD", "days": {} }""")]
    [InlineData("""{ "month": "2026-10", "currency": "USD", "days": { "yesterday": { "first": 1, "last": 2 } } }""")]
    [InlineData("""{ "month": "2026-10", "currency": "USD" }""")]
    public void ADamagedFileIsAnEmptyLedger(string json)
    {
        File.WriteAllText(Path_, json);

        Assert.Same(SpendLedger.Empty, new SpendLedgerStore(Path_).Load());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
