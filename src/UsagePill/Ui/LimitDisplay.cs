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
