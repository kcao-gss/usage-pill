namespace UsagePill.Core;

public enum LimitKind { Session, WeeklyAll, WeeklyScoped, Unknown }

public enum Severity { Normal, Warning, Critical }

/// <summary>One metered limit, for example the 5 hour session window.</summary>
public sealed record UsageLimit(
    LimitKind Kind,
    double Percent,
    Severity ApiSeverity,
    DateTimeOffset? ResetsAt,
    string? ScopeLabel);
