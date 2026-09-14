namespace UsagePill.Core;

public enum UsageStatus { Loading, Ok, Stale, NoCredentials, AuthExpired }

/// <summary>What the UI renders. Snapshot is the last good data, even when stale.</summary>
public sealed record UsageState(
    UsageStatus Status,
    UsageSnapshot? Snapshot,
    string? Message,
    DateTimeOffset? RetryAt)
{
    public static readonly UsageState Loading = new(UsageStatus.Loading, null, null, null);
}
