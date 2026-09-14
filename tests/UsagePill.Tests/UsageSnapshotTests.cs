using UsagePill.Core;

namespace UsagePill.Tests;

public class UsageSnapshotTests
{
    private static UsageLimit Limit(LimitKind kind, double percent) =>
        new(kind, percent, Severity.Normal, null, null);

    [Fact]
    public void FindReturnsTheLimitOfThatKind()
    {
        var snapshot = new UsageSnapshot(
            new[] { Limit(LimitKind.Session, 90), Limit(LimitKind.WeeklyAll, 17) },
            null,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(17, snapshot.Find(LimitKind.WeeklyAll)!.Percent);
    }

    [Fact]
    public void FindReturnsNullWhenTheKindIsAbsent()
    {
        var snapshot = new UsageSnapshot(new[] { Limit(LimitKind.Session, 90) }, null, DateTimeOffset.UnixEpoch);

        Assert.Null(snapshot.Find(LimitKind.WeeklyScoped));
    }
}
