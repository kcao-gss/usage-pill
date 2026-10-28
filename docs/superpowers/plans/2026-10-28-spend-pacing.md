# Spend Pacing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a spend-only account the pill shows three rings: today's share of the monthly cap, this week's share, and the month. The shares roll forward.

**Architecture:** A new `Pacing` folder holds pure month and day maths (`SpendMonth`), the app's own spend history (`SpendLedger`), the shares (`SpendBudget`), and the code that persists the history and keeps it current (`SpendLedgerStore` and `SpendTracker`). `App.Render` asks the tracker for a `SpendBudget` and hands it to the pill, the detail card and the tray next to the `UsageState`. In the UI, a `RingId` enum replaces the nullable `LimitKind` that stands for the spend ring today.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, WinForms `NotifyIcon`, `System.Text.Json`, `System.Collections.Immutable`, xunit 2.9.

**Spec:** `docs/superpowers/specs/2026-10-28-spend-pacing-design.md`. Read it first. Section numbers below refer to it.

## Global Constraints

- Build and test only through `./build.sh build` and `./build.sh test` from WSL, at the repo root `/home/kcao/Projects/usage-pill`. The wrapper runs the Windows SDK. Never use the WSL `dotnet`.
- To run one test class: `./build.sh test --filter "FullyQualifiedName~SpendMonthTests"`.
- `Nullable` is `enable` and `TreatWarningsAsErrors` is `true` in both projects.
- No em dash characters anywhere in code, comments, UI copy or docs. Use a plain hyphen.
- Commit after every task with a conventional-commit message. Never add a co-author line. Never edit `CHANGELOG.md` by hand.
- The budget month is the UTC calendar month. Days are local calendar days, and weeks run Monday to Sunday. Both are clipped to the month (spec section 3).
- Colours, verbatim from `RingGeometry`: red `#F2555A`, amber `#F5B73D`, grey `#6C7684`.
- UI copy, verbatim: ring labels `Today`, `This week`, and the English month name (`October`). Tooltip lines `Today 126% - $40 of $32`, `This week 41% - $90 of $217` and `October 47% - $470.00 / $1000`. Tray tooltip `Claude today 126% of $32`. Card footer `Resets Sat Oct 31, 7:00 PM`. Card axis caption `dashed: $31 a day`.
- Time zone IDs in tests are Windows IDs (`Central Standard Time`, `Tokyo Standard Time`), because the tests run on Windows.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `src/UsagePill/Pacing/SpendMonth.cs` | Create | `Period`, `SpendDay`, `SpendMonth`: one UTC month cut into local days, plus week lookup |
| `src/UsagePill/Pacing/SpendLedger.cs` | Create | `SpendDayReading`, `SpendLedger`: per-day first and last totals, and `OpeningOf` |
| `src/UsagePill/Pacing/SpendBudget.cs` | Create | `SpendPace`, `SpendDayBar`, `SpendBudget.Compute` |
| `src/UsagePill/Settings/AtomicFile.cs` | Create | The temp-file-and-move write, extracted from `SettingsStore` |
| `src/UsagePill/Settings/SettingsStore.cs` | Modify | `Save` delegates to `AtomicFile` |
| `src/UsagePill/Pacing/SpendLedgerStore.cs` | Create | Loads and saves `spend-ledger.json` |
| `src/UsagePill/Pacing/SpendTracker.cs` | Create | Records each new spend-only snapshot, saves, and computes the budget |
| `src/UsagePill/Ui/RingGeometry.cs` | Modify | `PaceColorFor`, `FormatPacePercent` |
| `src/UsagePill/Ui/LimitDisplay.cs` | Modify | `RingId`, `RingReading`, the new `Reading`, money formatting |
| `src/UsagePill/Ui/PillWindow.xaml.cs` | Modify | Three spend rings, the text slot, the tooltip |
| `src/UsagePill/Ui/TrayIcon.cs` | Modify | Shows today's ring on a spend-only account |
| `src/UsagePill/Ui/DetailPopup.xaml.cs` | Modify | Today, This week and month rows, the daily bar strip, and the reset footer |
| `src/UsagePill/App.xaml.cs` | Modify | Owns the `SpendTracker` and passes the budget on |
| `README.md` | Modify | The "Accounts billed by spend only" section and its troubleshooting row |
| `tests/UsagePill.Tests/SpendMonthTests.cs` | Create | |
| `tests/UsagePill.Tests/SpendLedgerTests.cs` | Create | |
| `tests/UsagePill.Tests/SpendBudgetTests.cs` | Create | |
| `tests/UsagePill.Tests/SpendLedgerStoreTests.cs` | Create | |
| `tests/UsagePill.Tests/RingGeometryTests.cs` | Modify | Pace colour and percent |
| `tests/UsagePill.Tests/LimitDisplayTests.cs` | Create | Money-left formatting |

---

### Task 1: Budget month and local days

**Files:**
- Create: `src/UsagePill/Pacing/SpendMonth.cs`
- Test: `tests/UsagePill.Tests/SpendMonthTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct Period(DateTimeOffset Start, DateTimeOffset End)` with `bool Contains(DateTimeOffset at)`
  - `readonly record struct SpendDay(DateOnly Date, Period Span)`
  - `sealed class SpendMonth`, with:
    - `Period Span`
    - `IReadOnlyList<SpendDay> Days`
    - `string Name`
    - `static SpendMonth Containing(DateTimeOffset at, TimeZoneInfo zone)`
    - `int IndexOf(DateTimeOffset at)`
    - `(int First, int Last) WeekOf(int index)`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run the tests and check that they fail**

Run: `./build.sh test --filter "FullyQualifiedName~SpendMonthTests"`
Expected: a build error. The type or namespace `UsagePill.Pacing` / `SpendMonth` does not exist.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run the tests and check that they pass**

Run: `./build.sh test --filter "FullyQualifiedName~SpendMonthTests"`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Pacing/SpendMonth.cs tests/UsagePill.Tests/SpendMonthTests.cs
git commit -m "feat: budget month split into local days"
```

---

### Task 2: Spend ledger

**Files:**
- Create: `src/UsagePill/Pacing/SpendLedger.cs`
- Test: `tests/UsagePill.Tests/SpendLedgerTests.cs`

**Interfaces:**
- Consumes: `UsagePill.Core.SpendInfo(decimal Used, decimal Limit, string Currency, Severity ApiSeverity)`
- Produces:
  - `sealed record SpendDayReading(decimal First, decimal Last)`
  - `sealed record SpendLedger(DateTimeOffset MonthStart, string Currency, ImmutableSortedDictionary<DateOnly, SpendDayReading> Days)`, with:
    - `static readonly SpendLedger Empty`
    - `SpendLedger Record(SpendInfo spend, DateTimeOffset monthStart, DateOnly date)`
    - `decimal? OpeningOf(DateOnly date, bool startsMonth)`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run the tests and check that they fail**

Run: `./build.sh test --filter "FullyQualifiedName~SpendLedgerTests"`
Expected: a build error. `SpendLedger` does not exist.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run the tests and check that they pass**

Run: `./build.sh test --filter "FullyQualifiedName~SpendLedgerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Pacing/SpendLedger.cs tests/UsagePill.Tests/SpendLedgerTests.cs
git commit -m "feat: spend ledger with per-day openings"
```

---

### Task 3: Roll-forward budget

**Files:**
- Create: `src/UsagePill/Pacing/SpendBudget.cs`
- Test: `tests/UsagePill.Tests/SpendBudgetTests.cs`

**Interfaces:**
- Consumes: `SpendMonth` (Task 1), `SpendLedger` (Task 2), `SpendInfo`
- Produces:
  - `sealed record SpendPace(decimal Spent, decimal Share)`, with `double Percent` and `decimal Left`
  - `sealed record SpendDayBar(DateOnly Date, decimal? Spent, decimal Share, bool IsToday, bool IsFuture)`, with `bool Over`
  - `sealed record SpendBudget(SpendInfo Spend, SpendPace Today, SpendPace Week, string MonthName, DateTimeOffset ResetsAt, decimal EvenSplit, IReadOnlyList<SpendDayBar> Days)`, with `static SpendBudget Compute(SpendInfo spend, SpendLedger ledger, SpendMonth month, int today)`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run the tests and check that they fail**

Run: `./build.sh test --filter "FullyQualifiedName~SpendBudgetTests"`
Expected: a build error. `SpendBudget` does not exist.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run the tests and check that they pass**

Run: `./build.sh test --filter "FullyQualifiedName~SpendBudgetTests"`
Expected: PASS. If `AQuietDayRaisesTheNextDaysShare` fails only on decimal precision, compare with `Math.Round(..., 10)` on both sides. Do not change the formula.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Pacing/SpendBudget.cs tests/UsagePill.Tests/SpendBudgetTests.cs
git commit -m "feat: roll-forward day and week spend shares"
```

---

### Task 4: Persist the ledger and keep it current

**Files:**
- Create: `src/UsagePill/Settings/AtomicFile.cs`
- Modify: `src/UsagePill/Settings/SettingsStore.cs` (the `Save` method, `SweepOrphanedTempFiles`, `TryDelete` and `OrphanAge` move out)
- Create: `src/UsagePill/Pacing/SpendLedgerStore.cs`
- Create: `src/UsagePill/Pacing/SpendTracker.cs`
- Test: `tests/UsagePill.Tests/SpendLedgerStoreTests.cs`

**Interfaces:**
- Consumes: `SpendLedger` (Task 2), `SpendMonth` (Task 1), `SpendBudget` (Task 3), `UsageState`
- Produces:
  - `static class AtomicFile` with `void WriteAllText(string path, string contents)`
  - `sealed class SpendLedgerStore(string path)`, with:
    - `static string DefaultPath()`
    - `SpendLedger Load()`
    - `void Save(SpendLedger ledger)`
  - `sealed class SpendTracker(SpendLedgerStore store, TimeZoneInfo zone)` with `SpendBudget? Update(UsageState state)`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

Note: `Assert.Equal(ledger.Days, loaded.Days)` compares the dictionaries entry by entry (`SpendDayReading` is a record).

- [ ] **Step 2: Run the tests and check that they fail**

Run: `./build.sh test --filter "FullyQualifiedName~SpendLedgerStoreTests"`
Expected: a build error. `SpendLedgerStore` does not exist.

- [ ] **Step 3: Extract `AtomicFile`**

Create `src/UsagePill/Settings/AtomicFile.cs` by moving `OrphanAge`, `SweepOrphanedTempFiles` and `TryDelete` out of `SettingsStore` unchanged. The only change is that the sweep takes the target path:

```csharp
using System.IO;

namespace UsagePill.Settings;

/// <summary>Writes a file so a reader only ever sees the old contents or the new, never half of either.</summary>
public static class AtomicFile
{
    /// <summary>A temp file this old cannot belong to a save still in flight.</summary>
    private static readonly TimeSpan OrphanAge = TimeSpan.FromMinutes(5);

    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        SweepOrphanedTempFiles(path, directory);

        // A unique name keeps concurrent saves off each other's temp file.
        var temp = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// A save killed mid-flight leaves its uniquely named temp file behind for good, so drop
    /// any sibling temp file old enough that no live save can still own it. Best effort only.
    /// </summary>
    private static void SweepOrphanedTempFiles(string path, string? directory)
    {
        var cutoff = DateTime.UtcNow - OrphanAge;
        try
        {
            var folder = string.IsNullOrEmpty(directory) ? "." : directory;
            foreach (var orphan in Directory.EnumerateFiles(folder, Path.GetFileName(path) + ".*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(orphan) < cutoff) TryDelete(orphan);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Sweeping is housekeeping: never let it fail a save.
        }
    }

    /// <summary>Deletes a temp file without letting the cleanup mask the real failure.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file another process holds open stays; the sweep picks it up later.
        }
    }
}
```

In `SettingsStore.cs`, delete `OrphanAge`, `SweepOrphanedTempFiles` and `TryDelete`, and replace `Save` with:

```csharp
    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(settings.Normalized(), Options));
```

Run: `./build.sh test --filter "FullyQualifiedName~SettingsTests"`
Expected: PASS. The existing temp-file sweep tests now cover `AtomicFile`.

- [ ] **Step 4: Implement `SpendLedgerStore`**

```csharp
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using UsagePill.Settings;

namespace UsagePill.Pacing;

/// <summary>Loads and saves the spend ledger as JSON next to settings.json (spec section 5).</summary>
public sealed class SpendLedgerStore
{
    private const string MonthFormat = "yyyy-MM";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;

    public SpendLedgerStore(string path) => _path = path;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "usage-pill",
        "spend-ledger.json");

    public SpendLedger Load()
    {
        try
        {
            var file = JsonSerializer.Deserialize<LedgerFile>(File.ReadAllText(_path), Options);
            if (file?.Month is null || file.Currency is null || file.Days is null) return SpendLedger.Empty;

            var month = DateTime.ParseExact(file.Month, MonthFormat, CultureInfo.InvariantCulture);
            var days = file.Days.ToImmutableSortedDictionary(
                pair => DateOnly.ParseExact(pair.Key, DateFormat, CultureInfo.InvariantCulture),
                pair => new SpendDayReading(pair.Value.First, pair.Value.Last));
            return new SpendLedger(new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero), file.Currency, days);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or FormatException)
        {
            // A missing or damaged file is not an error: the ledger starts over, as settings do.
            return SpendLedger.Empty;
        }
    }

    public void Save(SpendLedger ledger)
    {
        var file = new LedgerFile(
            ledger.MonthStart.UtcDateTime.ToString(MonthFormat, CultureInfo.InvariantCulture),
            ledger.Currency,
            ledger.Days.ToDictionary(
                pair => pair.Key.ToString(DateFormat, CultureInfo.InvariantCulture),
                pair => new DayEntry(pair.Value.First, pair.Value.Last)));
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, Options));
    }

    private sealed record LedgerFile(string? Month, string? Currency, Dictionary<string, DayEntry>? Days);

    private sealed record DayEntry(decimal First, decimal Last);
}
```

- [ ] **Step 5: Implement `SpendTracker`**

```csharp
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
```

- [ ] **Step 6: Run the tests and check that they pass**

Run: `./build.sh test`
Expected: PASS, the whole suite, including `SettingsTests`.

- [ ] **Step 7: Commit**

```bash
git add src/UsagePill/Settings/AtomicFile.cs src/UsagePill/Settings/SettingsStore.cs src/UsagePill/Pacing/SpendLedgerStore.cs src/UsagePill/Pacing/SpendTracker.cs tests/UsagePill.Tests/SpendLedgerStoreTests.cs
git commit -m "feat: persist the spend ledger and track the budget"
```

---

### Task 5: Ring identity, pace colour and formatting

**Files:**
- Modify: `src/UsagePill/Ui/RingGeometry.cs` (add two methods after `FormatPercent`)
- Modify: `src/UsagePill/Ui/LimitDisplay.cs` (whole file)
- Test: `tests/UsagePill.Tests/RingGeometryTests.cs`, `tests/UsagePill.Tests/LimitDisplayTests.cs`

**Interfaces:**
- Consumes: `SpendBudget`, `SpendPace` (Task 3)
- Produces:
  - `RingGeometry.PaceColorFor(double percent, int warnThresholdPercent, Color normal)`
  - `RingGeometry.FormatPacePercent(double percent)`
  - `enum RingId { Session, WeeklyAll, WeeklyScoped, SpendToday, SpendWeek, SpendMonth }`
  - `readonly record struct RingReading(double Percent, Severity Severity, bool IsPace)`, with `Color ColorFor(int warnThresholdPercent, Color normal)` and `string Text`
  - `LimitDisplay.Order : (RingId Ring, LimitKind Kind, string Label)[]`
  - `LimitDisplay.SpendRings : RingId[]`
  - `LimitDisplay.TodayLabel = "Today"`, `LimitDisplay.WeekLabel = "This week"`
  - `LimitDisplay.Reading(UsageSnapshot? snapshot, SpendBudget? budget, RingId ring) : RingReading?`
  - `LimitDisplay.FormatMoney(decimal amount, string currency)`
  - `LimitDisplay.FormatPace(SpendPace pace, string currency)`
  - `LimitDisplay.FormatLeft(SpendPace pace, string currency)`
  - `LimitDisplay.FormatSpend(SpendInfo spend)` (unchanged)
- Removes: `LimitDisplay.SpendLabel`, `LimitDisplay.FormatSpent`, and the `Reading(UsageSnapshot?, LimitKind?)` overload. Tasks 6 and 7 update every caller.

This task leaves the app project failing to build until Task 6 updates the callers, so Tasks 5 and 6 share one commit. Do not commit at the end of this task.

- [ ] **Step 1: Write the failing tests**

Append to `RingGeometryTests`:

```csharp
    [Theory]
    [InlineData(74.9, "normal")]
    [InlineData(75, "amber")]
    [InlineData(100.9, "amber")] // shows as 100, so it does not read as over
    [InlineData(101, "red")]
    [InlineData(450, "red")]
    public void APaceRingGoesRedOnlyOnceItShowsMoreThan100(double percent, string expected)
    {
        var colour = RingGeometry.PaceColorFor(percent, 75, Blue);

        Assert.Equal(expected switch { "red" => RingGeometry.Red, "amber" => RingGeometry.Amber, _ => Blue }, colour);
    }

    [Theory]
    [InlineData(126.3, "126")]
    [InlineData(0, "0")]
    [InlineData(1500, "999")]
    public void PacePercentsRunPast100UpTo999(double percent, string expected)
    {
        Assert.Equal(expected, RingGeometry.FormatPacePercent(percent));
    }
```

Create `tests/UsagePill.Tests/LimitDisplayTests.cs`:

```csharp
using UsagePill.Pacing;
using UsagePill.Ui;

namespace UsagePill.Tests;

public class LimitDisplayTests
{
    [Theory]
    [InlineData(31.67, 12.4, "$19")]   // left rounds down, never promising money that is not there
    [InlineData(31.67, 39.2, "+$8")]   // over rounds up, never hiding overspend
    [InlineData(32, 32, "$0")]
    public void MoneyLeftRoundsTowardCaution(double share, double spent, string expected)
    {
        Assert.Equal(expected, LimitDisplay.FormatLeft(new SpendPace((decimal)spent, (decimal)share), "USD"));
    }

    [Fact]
    public void OtherCurrenciesUseTheirCode()
    {
        Assert.Equal("EUR 19", LimitDisplay.FormatLeft(new SpendPace(12.4m, 31.67m), "EUR"));
    }
}
```

- [ ] **Step 2: Implement `RingGeometry` additions**

Insert after `FormatPercent`:

```csharp
    /// <summary>
    /// A pace ring is a share of a spend budget. It can run past 100, and going over is what it
    /// must flag, so it is red once the number shown passes 100 rather than when the API says so.
    /// </summary>
    public static Color PaceColorFor(double percent, int warnThresholdPercent, Color normal) => percent switch
    {
        >= 101 => Red,
        _ when percent >= warnThresholdPercent => Amber,
        _ => normal,
    };

    /// <summary>Whole numbers past 100, capped at three digits so the number still fits the ring.</summary>
    public static string FormatPacePercent(double percent) =>
        ((int)Math.Floor(Math.Clamp(percent, 0, 999))).ToString(CultureInfo.InvariantCulture);
```

- [ ] **Step 3: Replace `LimitDisplay.cs`**

```csharp
using System.Windows.Media;
using UsagePill.Core;
using UsagePill.Pacing;

namespace UsagePill.Ui;

/// <summary>Which ring a gauge shows. The first three are an account's limits, and the last three are a spend-only account's budget.</summary>
public enum RingId { Session, WeeklyAll, WeeklyScoped, SpendToday, SpendWeek, SpendMonth }

/// <summary>
/// What one ring shows. A pace reading is a share of a spend budget: it runs past 100 and is
/// coloured by <see cref="RingGeometry.PaceColorFor"/> instead of the API's severity.
/// </summary>
public readonly record struct RingReading(double Percent, Severity Severity, bool IsPace)
{
    public Color ColorFor(int warnThresholdPercent, Color normal) => IsPace
        ? RingGeometry.PaceColorFor(Percent, warnThresholdPercent, normal)
        : RingGeometry.ColorFor(Percent, Severity, warnThresholdPercent, normal);

    public string Text => IsPace ? RingGeometry.FormatPacePercent(Percent) : RingGeometry.FormatPercent(Percent);
}

/// <summary>
/// Ring order, labels, and status copy shared by the pill window, the detail card and the tray,
/// so they can never silently disagree about what a ring means or how a status reads. Ring
/// order is the spec's only means of identifying the rings (section 5.1).
/// </summary>
public static class LimitDisplay
{
    public static readonly (RingId Ring, LimitKind Kind, string Label)[] Order =
    {
        (RingId.Session, LimitKind.Session, "Session"),
        (RingId.WeeklyAll, LimitKind.WeeklyAll, "Weekly, all models"),
        (RingId.WeeklyScoped, LimitKind.WeeklyScoped, "Weekly, per model"),
    };

    /// <summary>The rings of a spend-only account (see <see cref="UsageSnapshot.IsSpendOnly"/>), short to long like <see cref="Order"/>.</summary>
    public static readonly RingId[] SpendRings = { RingId.SpendToday, RingId.SpendWeek, RingId.SpendMonth };

    public const string TodayLabel = "Today";
    public const string WeekLabel = "This week";

    public const string NoCredentialsMessage = "Claude Code not logged in";
    public const string AuthExpiredMessage = "Login expired - start Claude Code to refresh";
    public const string MissingScopeMessage = "Login lacks usage access - run /login in Claude Code";

    /// <summary>A weekly-scoped limit reports its own model-family label from the API; every
    /// other kind keeps the static label from <see cref="Order"/>.</summary>
    public static string NameFor(LimitKind kind, string label, string? scopeLabel) =>
        kind == LimitKind.WeeklyScoped && scopeLabel is { } scope ? $"Weekly, {scope}" : label;

    /// <summary>What <paramref name="ring"/> shows, or null when there is no data for it.</summary>
    public static RingReading? Reading(UsageSnapshot? snapshot, SpendBudget? budget, RingId ring)
    {
        if (snapshot is null) return null;
        return ring switch
        {
            RingId.Session => Limit(snapshot, LimitKind.Session),
            RingId.WeeklyAll => Limit(snapshot, LimitKind.WeeklyAll),
            RingId.WeeklyScoped => Limit(snapshot, LimitKind.WeeklyScoped),
            RingId.SpendToday => budget is { } today ? new RingReading(today.Today.Percent, Severity.Normal, IsPace: true) : null,
            RingId.SpendWeek => budget is { } week ? new RingReading(week.Week.Percent, Severity.Normal, IsPace: true) : null,
            RingId.SpendMonth => snapshot.Spend is { } spend ? new RingReading(spend.Percent, spend.ApiSeverity, IsPace: false) : null,
            _ => throw new ArgumentOutOfRangeException(nameof(ring), ring, null),
        };
    }

    private static RingReading? Limit(UsageSnapshot snapshot, LimitKind kind) =>
        snapshot.Find(kind) is { } limit ? new RingReading(limit.Percent, limit.ApiSeverity, IsPace: false) : null;

    public static string FormatSpend(SpendInfo spend)
    {
        var symbol = CurrencySymbol(spend.Currency);
        return $"{symbol}{spend.Used:0.00} / {symbol}{spend.Limit:0}";
    }

    /// <summary>Whole units: <c>$32</c>.</summary>
    public static string FormatMoney(decimal amount, string currency) => $"{CurrencySymbol(currency)}{amount:0}";

    /// <summary><c>$40 of $32</c>.</summary>
    public static string FormatPace(SpendPace pace, string currency) =>
        $"{FormatMoney(pace.Spent, currency)} of {FormatMoney(pace.Share, currency)}";

    /// <summary>
    /// What is left of a share, <c>$19</c>, or how far over it, <c>+$8</c>. Short enough for the
    /// pill's text slot. Left rounds down and over rounds up, so the pill never overstates the
    /// money there is.
    /// </summary>
    public static string FormatLeft(SpendPace pace, string currency) => pace.Left >= 0
        ? FormatMoney(Math.Floor(pace.Left), currency)
        : "+" + FormatMoney(Math.Ceiling(-pace.Left), currency);

    private static string CurrencySymbol(string currency) => currency == "USD" ? "$" : currency + " ";
}
```

Proceed directly to Task 6. The tests run there, once the callers compile.

---

### Task 6: Pill, tray and wiring

**Files:**
- Modify: `src/UsagePill/Ui/PillWindow.xaml.cs`
- Modify: `src/UsagePill/Ui/TrayIcon.cs:110-128`
- Modify: `src/UsagePill/Ui/DetailPopup.xaml.cs` (only enough to compile. Task 7 builds the card)
- Modify: `src/UsagePill/App.xaml.cs`

**Interfaces:**
- Consumes: everything in Task 5, plus `SpendTracker` (Task 4)
- Produces:
  - `PillWindow.Apply(UsageState state, SpendBudget? budget)`
  - `TrayIcon.Apply(UsageState state, SpendBudget? budget, AppSettings settings)`
  - `DetailPopup.Apply(UsageState state, SpendBudget? budget, string providerName)`

- [ ] **Step 1: `PillWindow`**

1. Add `using UsagePill.Pacing;`.
2. Replace the `_gauges` field and its comment with:

```csharp
    private readonly List<(RingId Ring, RingGauge Gauge)> _gauges = new();
```

3. Add a field after `_state`: `private SpendBudget? _budget;`
4. Replace `Apply(UsageState state)` with:

```csharp
    public void Apply(UsageState state, SpendBudget? budget)
    {
        _state = state;
        _budget = budget;

        // Follows the last good snapshot, which the state keeps through errors, so a failed poll
        // never flips the pill between its spend rings and its limit rings.
        var spendOnly = state.Snapshot?.IsSpendOnly == true;
        if (spendOnly != _spendOnly)
        {
            _spendOnly = spendOnly;
            Rebuild();
            return;
        }

        Render();
    }
```

5. In `Rebuild`, change `foreach (var kind in RingKinds())` to `foreach (var ring in RingIds())`, and `_gauges.Add((kind, gauge));` to `_gauges.Add((ring, gauge));`.
6. Replace `RingKinds()` with:

```csharp
    /// <summary>A spend-only account shows its budget rings, whatever the ring settings say.</summary>
    private IEnumerable<RingId> RingIds()
    {
        if (_spendOnly) return LimitDisplay.SpendRings;
        return LimitDisplay.Order.Where(o => IsRingEnabled(o.Kind)).Select(o => o.Ring);
    }
```

7. In `Render`, replace the loop body from `var (kind, gauge) = _gauges[i];` through the end of the loop. Keep the stale-dot, `!` and grey branches as they are, apart from the renamed variables:

```csharp
            var (ring, gauge) = _gauges[i];
            var reading = LimitDisplay.Reading(snapshot, _budget, ring);

            gauge.ShowStaleDot = stale && i == 0;

            if (_state.Status is UsageStatus.AuthExpired or UsageStatus.MissingScope && i == 0)
            {
                gauge.Percent = 100;
                gauge.RingColor = RingGeometry.Amber;
                gauge.Text = "!";
                SetTextOpacity(gauge, 1.0);
                continue;
            }

            if (reading is not { } value)
            {
                gauge.Percent = 0;
                gauge.RingColor = RingGeometry.Grey;
                gauge.Text = _state.Status == UsageStatus.Loading ? "--" : "-";
                SetTextOpacity(gauge, 0.5);
                continue;
            }

            gauge.Percent = value.Percent;
            gauge.RingColor = value.ColorFor(_settings.WarnThresholdPercent, PillTheme.ParseRingColor(_settings.RingColor));
            gauge.Text = value.Text;
            SetTextOpacity(gauge, 1.0);
```

8. Replace the `_resetText` block in `Render` with:

```csharp
        if (_resetText is not null)
        {
            // A spend cap reports no reset time, so the slot shows what is left of today's share.
            _resetText.Text = _spendOnly
                ? snapshot is not null && _budget is { } budget ? LimitDisplay.FormatLeft(budget.Today, budget.Spend.Currency) : "-"
                : snapshot?.Find(LimitKind.Session)?.ResetsAt is { } resetsAt ? RingGeometry.FormatResetIn(resetsAt - DateTimeOffset.UtcNow) : "-";
        }
```

9. In `BuildTooltip`, change `foreach (var (kind, label) in LimitDisplay.Order)` to `foreach (var (_, kind, label) in LimitDisplay.Order)`, and replace the `if (snapshot is { IsSpendOnly: true, Spend: { } spent })` block with:

```csharp
        if (snapshot is { IsSpendOnly: true, Spend: { } spent } && _budget is { } budget)
        {
            AppendPace(text, LimitDisplay.TodayLabel, budget.Today, spent.Currency);
            AppendPace(text, LimitDisplay.WeekLabel, budget.Week, spent.Currency);
            text.Append(budget.MonthName).Append(' ').Append(RingGeometry.FormatPercent(spent.Percent)).Append("% - ")
                .Append(LimitDisplay.FormatSpend(spent)).AppendLine();
        }
```

and add the helper after `BuildTooltip`:

```csharp
    private static void AppendPace(StringBuilder text, string label, SpendPace pace, string currency) =>
        text.Append(label).Append(' ').Append(RingGeometry.FormatPacePercent(pace.Percent)).Append("% - ")
            .Append(LimitDisplay.FormatPace(pace, currency)).AppendLine();
```

- [ ] **Step 2: `TrayIcon.Apply`**

Add `using UsagePill.Pacing;`, then replace `Apply`:

```csharp
    public void Apply(UsageState state, SpendBudget? budget, AppSettings settings)
    {
        // The headline ring: the pill's first ring, so the session window, or today's share of a
        // spend-only account's budget.
        var spendOnly = state.Snapshot?.IsSpendOnly == true;
        var reading = LimitDisplay.Reading(state.Snapshot, budget, spendOnly ? RingId.SpendToday : RingId.Session);
        var text = reading?.Text ?? "-";
        var wpfColor = reading?.ColorFor(settings.WarnThresholdPercent, PillTheme.ParseRingColor(settings.RingColor)) ?? RingGeometry.Grey;

        _icon.Text = reading is null
            ? "Usage Pill - no data"
            : spendOnly && budget is { } b
                ? $"Claude today {text}% of {LimitDisplay.FormatMoney(b.Today.Share, b.Spend.Currency)}"
                : $"Claude session {text}%";

        var (next, handle) = Render(text, Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B), reading?.Percent ?? 0);
        _icon.Icon = next;
        _current?.Dispose();
        if (_currentIconHandle != IntPtr.Zero) NativeMethods.DestroyIcon(_currentIconHandle);
        _current = next;
        _currentIconHandle = handle;
    }
```

Also change the class summary to `/// <summary>Tray icon that draws the pill's first ring, plus the shared context menu.</summary>`.

- [ ] **Step 3: `DetailPopup`, just enough to compile**

Add `using UsagePill.Pacing;`. Change the signature to `public void Apply(UsageState state, SpendBudget? budget, string providerName)`, and update the `<see cref>` in the `Apply(AppSettings)` doc comment to `Apply(UsageState, SpendBudget, string)`. Change `var (kind, label) = LimitDisplay.Order[i];` to `var (_, kind, label) = LimitDisplay.Order[i];`. In the spend-only branch, replace `LimitDisplay.SpendLabel` with `budget?.MonthName ?? "Monthly spend"`. Task 7 replaces that branch.

- [ ] **Step 4: `App`**

1. Add `using UsagePill.Pacing;`.
2. Add a field after `_provider`: `private SpendTracker _spend = null!;`
3. In `BuildAndStart`, right after `_settings = _settingsStore.Load();` (plus its startup reconciliation block), add:

```csharp
        _spend = new SpendTracker(new SpendLedgerStore(SpendLedgerStore.DefaultPath()), TimeZoneInfo.Local);
```

4. Replace `Render`:

```csharp
    private void Render(UsageState state)
    {
        var budget = _spend.Update(state);
        _pill.Apply(state, budget);
        _detail.Apply(state, budget, _provider.DisplayName);
        _tray.Apply(state, budget, _settings);
    }
```

- [ ] **Step 5: Build, test and commit**

Run: `./build.sh build && ./build.sh test`
Expected: the build has 0 warnings and 0 errors, and all tests pass, including the new `RingGeometryTests` and `LimitDisplayTests`.

```bash
git add src/UsagePill tests/UsagePill.Tests/RingGeometryTests.cs tests/UsagePill.Tests/LimitDisplayTests.cs
git commit -m "feat: today, week and month rings for spend-only accounts"
```

---

### Task 7: Detail card budget rows and daily bars

**Files:**
- Modify: `src/UsagePill/Ui/DetailPopup.xaml.cs`

**Interfaces:**
- Consumes: `SpendBudget`, `SpendDayBar` (Task 3), `LimitDisplay.FormatPace`, `LimitDisplay.FormatMoney`, `RingReading` (Task 5)

The card's layout matches the prototype's card sketch (`.lavish/spend-pill.html`, section "The detail card"):
- three rows with meters;
- a 60 px bar strip with 2 px gaps;
- empty future stubs 2 px tall at `#FFFFFF` 8%;
- today's bar outlined in the card text colour;
- a dashed even-split line at `#FFFFFF` 45%;
- an axis caption row: first date, `dashed: $31 a day`, last date.

The card body is 246 px wide (`DetailPopup.xaml`).

- [ ] **Step 1: Generalise `Footer` to two optional strings**

Replace `Footer(DateTimeOffset? sessionResetsAt, DateTimeOffset? weeklyResetsAt)` with:

```csharp
    private static UIElement Footer(string? left, string? right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0, 9, 0, 0),
            Child = grid,
        };

        if (left is not null)
        {
            var text = Muted(left);
            text.Margin = new Thickness(0);
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);
        }

        if (right is not null)
        {
            var text = Muted(right);
            text.Margin = new Thickness(0);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }

        return border;
    }
```

- [ ] **Step 2: Replace the body of `Apply(UsageState, SpendBudget?, string)` from `var snapshot = state.Snapshot;` to the `Footer` call**

```csharp
        var snapshot = state.Snapshot;
        string? footerLeft = null;
        string? footerRight = null;

        for (var i = 0; i < LimitDisplay.Order.Length; i++)
        {
            var (_, kind, label) = LimitDisplay.Order[i];
            var limit = snapshot.Find(kind);
            if (limit is null) continue;

            var name = LimitDisplay.NameFor(kind, label, limit.ScopeLabel);
            var color = RingGeometry.ColorFor(limit.Percent, limit.ApiSeverity, _settings.WarnThresholdPercent, _ringColor);
            Body.Children.Add(Row(name, Ordinals[i], RingGeometry.FormatPercent(limit.Percent) + "%", color));
            Body.Children.Add(Meter(limit.Percent, color));

            if (limit.ResetsAt is not { } resets) continue;
            if (kind == LimitKind.Session) footerLeft = $"Session resets {resets.ToLocalTime():HH:mm}";
            else footerRight ??= $"Weekly {resets.ToLocalTime():ddd HH:mm}";
        }

        if (snapshot.Spend is { } spend)
        {
            if (!snapshot.IsSpendOnly)
            {
                Body.Children.Add(Row("Extra credits", null, LimitDisplay.FormatSpend(spend), RingGeometry.Grey));
            }
            else if (budget is { } b)
            {
                AddBudget(snapshot, b);
                footerLeft = "Resets " + b.ResetsAt.ToLocalTime().ToString("ddd MMM d, h:mm tt", CultureInfo.InvariantCulture);
            }
        }

        if (footerLeft is not null || footerRight is not null)
        {
            Body.Children.Add(Footer(footerLeft, footerRight));
        }
```

Add `using System.Globalization;` at the top.

- [ ] **Step 3: Add the budget rows and the bar strip**

Add these methods after `Meter`:

```csharp
    private void AddBudget(UsageSnapshot snapshot, SpendBudget budget)
    {
        var currency = budget.Spend.Currency;
        AddPaceRow(LimitDisplay.TodayLabel, budget.Today, currency, LimitDisplay.Reading(snapshot, budget, RingId.SpendToday));
        AddPaceRow(LimitDisplay.WeekLabel, budget.Week, currency, LimitDisplay.Reading(snapshot, budget, RingId.SpendWeek));

        var month = LimitDisplay.Reading(snapshot, budget, RingId.SpendMonth);
        var monthColor = month?.ColorFor(_settings.WarnThresholdPercent, _ringColor) ?? RingGeometry.Grey;
        Body.Children.Add(Row(budget.MonthName, null, LimitDisplay.FormatSpend(budget.Spend), monthColor));
        Body.Children.Add(Meter(budget.Spend.Percent, monthColor));

        Body.Children.Add(DailyBars(budget));
    }

    private void AddPaceRow(string label, SpendPace pace, string currency, RingReading? reading)
    {
        var color = reading?.ColorFor(_settings.WarnThresholdPercent, _ringColor) ?? RingGeometry.Grey;
        Body.Children.Add(Row(label, null, LimitDisplay.FormatPace(pace, currency), color));
        Body.Children.Add(Meter(pace.Percent, color));
    }

    /// <summary>
    /// One bar per day of the budget month: red on days that went over their share, outlined for
    /// today, a stub for days still to come, and a dashed line at the even split.
    /// </summary>
    private UIElement DailyBars(SpendBudget budget)
    {
        const double height = 60;
        const double gap = 2;
        var width = Body.Width;
        var days = budget.Days;
        var slot = (width + gap) / days.Count;
        var barWidth = slot - gap;
        var currency = budget.Spend.Currency;

        var peak = Math.Max(budget.EvenSplit, days.Max(d => d.Spent ?? 0));
        double Scale(decimal amount) => peak > 0 ? (double)(amount / peak) * height : 0;

        var canvas = new Canvas { Width = width, Height = height, Margin = new Thickness(0, 4, 0, 0), ClipToBounds = false };
        var text = Color.FromRgb(0xEE, 0xF1, 0xF6);

        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var left = i * slot;

            if (day.IsFuture)
            {
                Place(canvas, new Border { Width = barWidth, Height = 2, Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)) }, left, height - 2);
                continue;
            }

            if (day.Spent is not { } spent) continue;

            var barHeight = Math.Max(1, Scale(spent));
            var bar = new Border
            {
                Width = barWidth,
                Height = barHeight,
                CornerRadius = new CornerRadius(1.5, 1.5, 0, 0),
                Background = new SolidColorBrush(day.Over ? RingGeometry.Red : _ringColor),
                ToolTip = $"{ShortDate(day.Date)}: {LimitDisplay.FormatMoney(spent, currency)} of {LimitDisplay.FormatMoney(day.Share, currency)}",
            };
            Place(canvas, bar, left, height - barHeight);

            if (day.IsToday)
            {
                var outline = new Border
                {
                    Width = barWidth + 2,
                    Height = barHeight + 1,
                    CornerRadius = new CornerRadius(2.5, 2.5, 0, 0),
                    BorderBrush = new SolidColorBrush(text),
                    BorderThickness = new Thickness(1, 1, 1, 0),
                    IsHitTestVisible = false,
                };
                Place(canvas, outline, left - 1, height - barHeight - 1);
            }
        }

        var evenY = Math.Round(height - Scale(budget.EvenSplit)) + 0.5;
        canvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 0, X2 = width, Y1 = evenY, Y2 = evenY,
            Stroke = new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            IsHitTestVisible = false,
        });

        var axis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        axis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        axis.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        axis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddAxisLabel(axis, ShortDate(days[0].Date), 0, TextAlignment.Left);
        AddAxisLabel(axis, $"dashed: {LimitDisplay.FormatMoney(budget.EvenSplit, currency)} a day", 1, TextAlignment.Center);
        AddAxisLabel(axis, ShortDate(days[^1].Date), 2, TextAlignment.Right);

        var strip = new StackPanel();
        strip.Children.Add(canvas);
        strip.Children.Add(axis);
        return strip;
    }

    private static void Place(Canvas canvas, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }

    private static void AddAxisLabel(Grid axis, string text, int column, TextAlignment alignment)
    {
        var label = Muted(text);
        label.Margin = new Thickness(0);
        label.TextAlignment = alignment;
        Grid.SetColumn(label, column);
        axis.Children.Add(label);
    }
```

Add the date helper next to `Place`, so the copy stays English like the rest of the card:

```csharp
    private static string ShortDate(DateOnly date) => date.ToString("MMM d", CultureInfo.InvariantCulture);
```

- [ ] **Step 4: Build and test**

Run: `./build.sh build && ./build.sh test`
Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/UsagePill/Ui/DetailPopup.xaml.cs
git commit -m "feat: detail card budget rows and daily spend bars"
```

---

### Task 8: README, end-to-end check, release

**Files:**
- Modify: `README.md` (the "Accounts billed by spend only" section, lines 47-62, and the troubleshooting row "A single ring instead of three")
- Modify: `src/UsagePill/UsagePill.csproj` (`<Version>`)

- [ ] **Step 1: Replace the README section**

Replace everything from `### Accounts billed by spend only` up to, but not including, `## Getting it running` with:

```markdown
### Accounts billed by spend only

Some accounts, such as enterprise seats billed by usage, have no 5 hour or weekly limit at
all: the endpoint reports only a monthly spend cap. The pill detects this from the response
itself and budgets the cap instead, whatever the ring settings say. It shows three rings:

- **Today**: today's spend against today's share of the cap.
- **This week**: this week's spend, Monday to Sunday, against the week's share.
- **Month**: the share of the monthly cap spent.

The cap resets at midnight UTC on the 1st, the same reset claude.ai shows. Today's share is
what was left of the cap when the day began, spread evenly over the days left in the month,
today included. So a quiet day raises every later day's share, and an expensive day lowers
them. The week's share works the same way. Days and weeks follow your local time, so the
month's first and last days are partial: in US Central time, October runs from 7 PM on
September 30 to 7 PM on October 31.

- The today and week rings read, for example, `126` when you are 26% over the share. They
  turn amber at the threshold and red above 100. The month ring turns amber at the threshold
  and red when the API reports the spend as critical.
- The tooltip reads, for example:

  ```
  Today 126% - $40 of $32
  This week 41% - $90 of $217
  October 47% - $470.00 / $1000
  ```

- The tray icon shows today's ring.
- The detail card shows a row for each ring, a bar for each day of the month, and when the cap
  resets. A bar is red on a day that went over its share, and a dashed line marks an even
  split of the cap.
- With **Show the reset time as text** on, the slot shows what is left of today's share, such
  as `$19`, or how far over it you are, such as `+$8`.

The usage endpoint keeps no daily history, so the pill keeps its own in
`%APPDATA%\usage-pill\spend-ledger.json`. It only knows what it has seen. On the day you
install it, today and this week count from the first reading, and spend made while the pill
is closed counts toward the next day it runs.

When the account gets limits again, the pill goes back to its limit rings on the next poll.
```

Replace the troubleshooting row that starts `| A single ring instead of three |` with:

```markdown
| Rings labelled Today, This week and the month instead of Session and Weekly | The account has no session or weekly limits, only a monthly spend cap. See [Accounts billed by spend only](#accounts-billed-by-spend-only). |
| Today and This week read 0 right after installing | The pill learns daily spend from its own readings, so on its first day it counts from the first one. |
```

Also check the "What the rings mean" section's "Red when the API itself reports that limit as critical" line. Leave it unchanged, because it describes limit rings. The spend section above covers pace rings.

- [ ] **Step 2: End-to-end on the real account**

1. Publish: `./build.sh publish`
2. Quit the running pill. Its tray menu has Quit. Alternatively run `powershell.exe -NoProfile -Command "Get-Process UsagePill -ErrorAction SilentlyContinue | Stop-Process"`.
3. Launch the published exe from cwd `/mnt/c/Windows/Temp`: `cmd.exe /c start "" "<publish dir>\UsagePill.exe"`, using the publish path printed by step 1.
4. Wait for the first poll (a few seconds), then screenshot the pill with `tools/screenshot.ps1`. Check that:
   - there are three rings (today, week, month);
   - today and week read 0, because the ledger is new;
   - the month ring reads the real percentage (15 or more);
   - the capsule is a pill again.
5. Hover the pill and screenshot the tooltip. It should show three lines in the format from Global Constraints.
6. Left-click the pill and screenshot the detail card. Check the three rows, the strip with today's outlined bar and future stubs, the axis caption, and the footer `Resets Sat Oct 31, 7:00 PM`.
7. Turn on **Show the reset time as text** and switch to horizontal orientation in Settings. Screenshot both. Then restore the user's settings (vertical, text off).
8. Check that `%APPDATA%\usage-pill\spend-ledger.json` exists and holds today's date.
9. Fix anything that looks off: clipping, misalignment, a 3-digit number overflowing the ring, uneven bar gaps. Then repeat from step 1.

- [ ] **Step 3: Release 0.6.0**

```bash
git add README.md
git commit -m "docs: spend pacing in the README"
```

Bump `<Version>` in `src/UsagePill/UsagePill.csproj` to `0.6.0`, then:

```bash
git add src/UsagePill/UsagePill.csproj
git commit -m "chore: release 0.6.0"
git tag v0.6.0
git push origin main --tags
```

CI (`.github/workflows/release.yml`) builds the release zips from the tag. Install the framework-dependent zip to `C:\Users\kcao\Documents\UsagePill-0.6.0-win-x64-framework-dependent\`. Point the Startup shortcut `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\usage-pill.lnk` at the new exe, and launch it.
