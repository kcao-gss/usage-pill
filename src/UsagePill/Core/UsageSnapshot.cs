namespace UsagePill.Core;

/// <summary>
/// Money spent against a monthly cap. <see cref="Percent"/> comes from the amounts rather than
/// the API's rounded percent, so it always agrees with the dollar figures shown beside it.
/// </summary>
public sealed record SpendInfo(decimal Used, decimal Limit, string Currency, Severity ApiSeverity)
{
    public double Percent => Limit > 0 ? (double)(Used / Limit * 100) : 0;
}

/// <summary>One successful read of a provider's usage endpoint.</summary>
public sealed record UsageSnapshot(
    IReadOnlyList<UsageLimit> Limits,
    SpendInfo? Spend,
    DateTimeOffset CapturedAt)
{
    public UsageLimit? Find(LimitKind kind)
    {
        for (var i = 0; i < Limits.Count; i++)
        {
            if (Limits[i].Kind == kind) return Limits[i];
        }
        return null;
    }

    /// <summary>
    /// An account billed only by usage, such as an enterprise seat, has no session or weekly
    /// window: the endpoint reports no limits at all, only the monthly spend cap.
    /// </summary>
    public bool IsSpendOnly => Limits.Count == 0 && Spend is not null;
}
