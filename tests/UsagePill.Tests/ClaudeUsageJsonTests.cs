using System.Globalization;
using System.IO;
using UsagePill.Claude;
using UsagePill.Core;

namespace UsagePill.Tests;

public class ClaudeUsageJsonTests
{
    private static readonly DateTimeOffset Captured = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "usage-response.json"));

    [Fact]
    public void ReadsTheSessionLimitFromTheLimitsArray()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        var session = snapshot.Find(LimitKind.Session)!;
        Assert.Equal(90, session.Percent);
        Assert.Equal(Severity.Critical, session.ApiSeverity);
        Assert.Equal(
            "2026-09-14T19:10:00",
            session.ResetsAt!.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ReadsThePerModelScopeLabel()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        Assert.Equal("Fable", snapshot.Find(LimitKind.WeeklyScoped)!.ScopeLabel);
    }

    [Fact]
    public void DropsLimitsWithAnUnknownKind()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        Assert.DoesNotContain(snapshot.Limits, l => l.Percent == 42);
        Assert.Equal(3, snapshot.Limits.Count);
    }

    [Fact]
    public void ReadsSpendInMajorUnits()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        Assert.Equal(0m, snapshot.Spend!.Used);
        Assert.Equal(200m, snapshot.Spend!.Limit);
        Assert.Equal("USD", snapshot.Spend!.Currency);
    }

    [Fact]
    public void FallsBackToFiveHourAndSevenDayWhenLimitsIsEmpty()
    {
        const string json = """
        {
          "five_hour": { "utilization": 55.0, "resets_at": "2026-09-14T19:10:00+00:00" },
          "seven_day": { "utilization": 12.0, "resets_at": "2026-09-17T02:00:00+00:00" },
          "limits": []
        }
        """;

        var snapshot = ClaudeUsageJson.Parse(json, Captured);

        Assert.Equal(55, snapshot.Find(LimitKind.Session)!.Percent);
        Assert.Equal(12, snapshot.Find(LimitKind.WeeklyAll)!.Percent);
    }

    [Fact]
    public void AnEmptyObjectProducesNoLimitsAndDoesNotThrow()
    {
        var snapshot = ClaudeUsageJson.Parse("{}", Captured);

        Assert.Empty(snapshot.Limits);
        Assert.Null(snapshot.Spend);
    }
}
