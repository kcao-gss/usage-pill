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
    public void AResponseWithOnlyASpendCapIsSpendOnly()
    {
        // The shape an enterprise seat gets: every window null, no limits, only the spend cap.
        const string json = """
        {
          "five_hour": null,
          "seven_day": null,
          "seven_day_opus": null,
          "extra_usage": { "is_enabled": true, "monthly_limit": 10000, "used_credits": 8693.0, "utilization": 86.93, "currency": "USD", "decimal_places": 2 },
          "limits": [],
          "spend": {
            "used": { "amount_minor": 8693, "currency": "USD", "exponent": 2 },
            "limit": { "amount_minor": 10000, "currency": "USD", "exponent": 2 },
            "percent": 87, "severity": "warning", "enabled": true
          }
        }
        """;

        var snapshot = ClaudeUsageJson.Parse(json, Captured);

        Assert.True(snapshot.IsSpendOnly);
        Assert.Equal(86.93, snapshot.Spend!.Percent, 6);
        Assert.Equal(Severity.Warning, snapshot.Spend!.ApiSeverity);
    }

    [Fact]
    public void AResponseWithLimitsAndSpendIsNotSpendOnly()
    {
        var snapshot = ClaudeUsageJson.Parse(Fixture(), Captured);

        Assert.False(snapshot.IsSpendOnly);
    }

    [Fact]
    public void LegacyWindowsKeepTheLimitRingsEvenWithASpendCap()
    {
        const string json = """
        {
          "five_hour": { "utilization": 55.0, "resets_at": "2026-09-14T19:10:00+00:00" },
          "limits": [],
          "spend": {
            "used": { "amount_minor": 500, "currency": "USD", "exponent": 2 },
            "limit": { "amount_minor": 20000, "currency": "USD", "exponent": 2 }
          }
        }
        """;

        var snapshot = ClaudeUsageJson.Parse(json, Captured);

        Assert.False(snapshot.IsSpendOnly);
        Assert.Equal(55, snapshot.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public void AnEmptyObjectProducesNoLimitsAndDoesNotThrow()
    {
        var snapshot = ClaudeUsageJson.Parse("{}", Captured);

        Assert.Empty(snapshot.Limits);
        Assert.Null(snapshot.Spend);
    }

    [Fact]
    public void ANullAmountDegradesSpendToNullWithoutThrowing()
    {
        const string json = """
        {
          "limits": [
            { "kind": "session", "percent": 30, "severity": "normal", "resets_at": null, "scope": null }
          ],
          "spend": {
            "used": { "amount_minor": null, "currency": "USD", "exponent": 2 },
            "limit": { "amount_minor": 20000, "currency": "USD", "exponent": 2 },
            "percent": 0
          }
        }
        """;

        var snapshot = ClaudeUsageJson.Parse(json, Captured);

        Assert.Null(snapshot.Spend);
        Assert.Equal(30, snapshot.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public void AStringAmountDegradesSpendToNullWithoutThrowing()
    {
        const string json = """
        {
          "limits": [
            { "kind": "session", "percent": 30, "severity": "normal", "resets_at": null, "scope": null }
          ],
          "spend": {
            "used": { "amount_minor": 0, "currency": "USD", "exponent": 2 },
            "limit": { "amount_minor": "20000", "currency": "USD", "exponent": 2 },
            "percent": 0
          }
        }
        """;

        var snapshot = ClaudeUsageJson.Parse(json, Captured);

        Assert.Null(snapshot.Spend);
        Assert.Equal(30, snapshot.Find(LimitKind.Session)!.Percent);
    }

    [Fact]
    public void AnOutOfRangeExponentFallsBackToTwo()
    {
        const string json = """
        {
          "limits": [],
          "spend": {
            "used": { "amount_minor": 500, "currency": "USD", "exponent": 40 },
            "limit": { "amount_minor": 20000, "currency": "USD", "exponent": -3 },
            "percent": 2
          }
        }
        """;

        var snapshot = ClaudeUsageJson.Parse(json, Captured);

        Assert.Equal(5m, snapshot.Spend!.Used);
        Assert.Equal(200m, snapshot.Spend!.Limit);
    }
}
