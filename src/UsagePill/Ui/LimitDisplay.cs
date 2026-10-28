using UsagePill.Core;

namespace UsagePill.Ui;

/// <summary>
/// Ring order, labels, and status copy shared by the pill window and the detail card, so the
/// two surfaces can never silently disagree about what a ring means or how a status reads.
/// Ring order is the spec's only means of identifying the rings (section 5.1).
/// </summary>
public static class LimitDisplay
{
    public static readonly (LimitKind Kind, string Label)[] Order =
    {
        (LimitKind.Session, "Session"),
        (LimitKind.WeeklyAll, "Weekly, all models"),
        (LimitKind.WeeklyScoped, "Weekly, per model"),
    };

    public const string NoCredentialsMessage = "Claude Code not logged in";
    public const string AuthExpiredMessage = "Login expired - start Claude Code to refresh";
    public const string MissingScopeMessage = "Login lacks usage access - run /login in Claude Code";

    /// <summary>The single ring of a spend-only account, see <see cref="UsageSnapshot.IsSpendOnly"/>.</summary>
    public const string SpendLabel = "Monthly spend";

    /// <summary>A weekly-scoped limit reports its own model-family label from the API; every
    /// other kind keeps the static label from <see cref="Order"/>.</summary>
    public static string NameFor(LimitKind kind, string label, string? scopeLabel) =>
        kind == LimitKind.WeeklyScoped && scopeLabel is { } scope ? $"Weekly, {scope}" : label;

    /// <summary>
    /// What a ring shows: the limit of <paramref name="kind"/>, or, for the spend ring of a
    /// spend-only account (a null kind), the monthly spend. Null when the snapshot lacks it.
    /// </summary>
    public static (double Percent, Severity Severity)? Reading(UsageSnapshot? snapshot, LimitKind? kind) => kind is { } limitKind
        ? snapshot?.Find(limitKind) is { } limit ? (limit.Percent, limit.ApiSeverity) : null
        : snapshot?.Spend is { } spend ? (spend.Percent, spend.ApiSeverity) : null;

    public static string FormatSpend(SpendInfo spend)
    {
        var symbol = CurrencySymbol(spend);
        return $"{symbol}{spend.Used:0.00} / {symbol}{spend.Limit:0}";
    }

    /// <summary>Whole units only, short enough for the pill's reset-time slot.</summary>
    public static string FormatSpent(SpendInfo spend) => $"{CurrencySymbol(spend)}{spend.Used:0}";

    private static string CurrencySymbol(SpendInfo spend) => spend.Currency == "USD" ? "$" : spend.Currency + " ";
}
