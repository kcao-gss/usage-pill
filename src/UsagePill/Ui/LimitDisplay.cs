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

    /// <summary>A weekly-scoped limit reports its own model-family label from the API; every
    /// other kind keeps the static label from <see cref="Order"/>.</summary>
    public static string NameFor(LimitKind kind, string label, string? scopeLabel) =>
        kind == LimitKind.WeeklyScoped && scopeLabel is { } scope ? $"Weekly, {scope}" : label;
}
