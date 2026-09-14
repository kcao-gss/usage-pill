namespace UsagePill.Core;

public sealed record SpendInfo(decimal Used, decimal Limit, string Currency);

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
}
