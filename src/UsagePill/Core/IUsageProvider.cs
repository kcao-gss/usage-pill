namespace UsagePill.Core;

/// <summary>One AI provider that can report usage limits.</summary>
public interface IUsageProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    Task<UsageSnapshot> FetchAsync(CancellationToken ct);
}
