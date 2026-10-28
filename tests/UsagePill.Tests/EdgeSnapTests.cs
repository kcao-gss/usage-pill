using System.Windows;
using UsagePill.Ui;

namespace UsagePill.Tests;

public class EdgeSnapTests
{
    private static readonly Rect Area = new(0, 0, 2560, 1440);
    private const double ShadowMargin = 22;

    [Fact]
    public void ACapsuleDraggedAsHighAsWindowsAllowsSnapsToTheTop()
    {
        // Windows stops the window's top at the work area's top, so the capsule inside its
        // shadow margin can come no closer to the edge than the margin.
        var landed = EdgeSnap.Snap(new Rect(1200, ShadowMargin, 44, 48), Area, ShadowMargin);

        Assert.Equal(0, landed.Y);
    }

    [Fact]
    public void ACapsuleWithinTheSnapDistanceOfTheTopReachSnapsToTheTop()
    {
        var landed = EdgeSnap.Snap(new Rect(1200, ShadowMargin + EdgeSnap.Distance, 44, 48), Area, ShadowMargin);

        Assert.Equal(0, landed.Y);
    }

    [Fact]
    public void ACapsuleBeyondTheSnapDistanceOfTheTopReachStaysPut()
    {
        var landed = EdgeSnap.Snap(new Rect(1200, ShadowMargin + EdgeSnap.Distance + 1, 44, 48), Area, ShadowMargin);

        Assert.Equal(ShadowMargin + EdgeSnap.Distance + 1, landed.Y);
    }

    [Fact]
    public void ACapsuleOverlappingTheTopOfAMonitorBelowAnotherSnapsDown()
    {
        // A monitor stacked above lets the window cross the edge, so the capsule can sit above it.
        var landed = EdgeSnap.Snap(new Rect(1200, Area.Top - 10, 44, 48), Area, ShadowMargin);

        Assert.Equal(0, landed.Y);
    }

    [Fact]
    public void ACapsuleNearTheRightEdgeSnapsFlushAgainstIt()
    {
        var landed = EdgeSnap.Snap(new Rect(2560 - 44 - 10, 600, 44, 48), Area, ShadowMargin);

        Assert.Equal(2560 - 44, landed.X);
        Assert.Equal(600, landed.Y);
    }

    [Fact]
    public void ACapsuleFlungPastTheRightEdgeLandsFlushAgainstIt()
    {
        var landed = EdgeSnap.Snap(new Rect(2560 - 44 + 21, 600, 44, 48), Area, ShadowMargin);

        Assert.Equal(2560 - 44, landed.X);
    }

    [Fact]
    public void ACapsuleFlungPastTheBottomEdgeLandsFlushAgainstIt()
    {
        var landed = EdgeSnap.Snap(new Rect(1200, 1440 - 48 + 30, 44, 48), Area, ShadowMargin);

        Assert.Equal(1440 - 48, landed.Y);
    }
}
