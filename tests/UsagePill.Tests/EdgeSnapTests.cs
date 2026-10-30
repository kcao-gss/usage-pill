using System.Windows;
using UsagePill.Ui;

namespace UsagePill.Tests;

public class EdgeSnapTests
{
    private static readonly Rect Area = new(0, 0, 2560, 1440);
    private const double ShadowMargin = 22;
    private const double Gap = EdgeSnap.Gap;

    [Fact]
    public void ACapsuleDraggedAsHighAsWindowsAllowsSnapsToTheTopGap()
    {
        // Windows stops the window's top at the work area's top, so the capsule inside its
        // shadow margin can come no closer to the edge than the margin.
        var landed = EdgeSnap.Snap(new Rect(1200, ShadowMargin, 44, 48), Area, ShadowMargin);

        Assert.Equal(Gap, landed.Y);
    }

    [Fact]
    public void ACapsuleWithinTheSnapDistanceOfTheTopReachSnapsToTheTopGap()
    {
        var landed = EdgeSnap.Snap(new Rect(1200, ShadowMargin + EdgeSnap.Distance, 44, 48), Area, ShadowMargin);

        Assert.Equal(Gap, landed.Y);
    }

    [Fact]
    public void ACapsuleBeyondTheSnapDistanceOfTheTopReachStaysPut()
    {
        var landed = EdgeSnap.Snap(new Rect(1200, ShadowMargin + EdgeSnap.Distance + 1, 44, 48), Area, ShadowMargin);

        Assert.Equal(ShadowMargin + EdgeSnap.Distance + 1, landed.Y);
    }

    [Fact]
    public void ACapsuleOverlappingTheTopOfAMonitorBelowAnotherSnapsDownToTheGap()
    {
        // A monitor stacked above lets the window cross the edge, so the capsule can sit above it.
        var landed = EdgeSnap.Snap(new Rect(1200, Area.Top - 10, 44, 48), Area, ShadowMargin);

        Assert.Equal(Gap, landed.Y);
    }

    [Fact]
    public void ACapsuleNearTheRightEdgeSnapsToTheGapInsideIt()
    {
        var landed = EdgeSnap.Snap(new Rect(2560 - Gap - 44 - EdgeSnap.Distance, 600, 44, 48), Area, ShadowMargin);

        Assert.Equal(2560 - Gap - 44, landed.X);
        Assert.Equal(600, landed.Y);
    }

    [Fact]
    public void ACapsuleJustBeyondTheSnapDistanceOfTheRightGapStaysPut()
    {
        var left = 2560 - Gap - 44 - EdgeSnap.Distance - 1;

        var landed = EdgeSnap.Snap(new Rect(left, 600, 44, 48), Area, ShadowMargin);

        Assert.Equal(left, landed.X);
    }

    [Fact]
    public void ACapsuleFlungPastTheRightEdgeLandsAtTheGapInsideIt()
    {
        var landed = EdgeSnap.Snap(new Rect(2560 - 44 + 21, 600, 44, 48), Area, ShadowMargin);

        Assert.Equal(2560 - Gap - 44, landed.X);
    }

    [Fact]
    public void ACapsuleFlungPastTheBottomEdgeLandsAtTheGapAboveIt()
    {
        var landed = EdgeSnap.Snap(new Rect(1200, 1440 - 48 + 30, 44, 48), Area, ShadowMargin);

        Assert.Equal(1440 - Gap - 48, landed.Y);
    }

    [Fact]
    public void ConstrainMovesACapsuleFlushAgainstAnEdgeOutToTheGap()
    {
        // A position saved before the gap existed, flush against the right edge.
        var kept = EdgeSnap.Constrain(new Rect(2560 - 44, 600, 44, 48), Area);

        Assert.Equal(new Point(2560 - Gap - 44, 600), kept);
    }

    [Fact]
    public void ConstrainLeavesACapsuleClearOfEveryEdgeWhereItIs()
    {
        var kept = EdgeSnap.Constrain(new Rect(1200, 600, 44, 48), Area);

        Assert.Equal(new Point(1200, 600), kept);
    }

    [Fact]
    public void ConstrainPinsACapsuleTooBigForTheAreaToTheTopLeftGap()
    {
        var kept = EdgeSnap.Constrain(new Rect(500, 500, 3000, 2000), Area);

        Assert.Equal(new Point(Gap, Gap), kept);
    }
}
