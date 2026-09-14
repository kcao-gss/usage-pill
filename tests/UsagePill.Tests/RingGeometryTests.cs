using System.Windows.Media;
using UsagePill.Core;
using UsagePill.Ui;

namespace UsagePill.Tests;

public class RingGeometryTests
{
    [Theory]
    [InlineData(24, 2.256, 8.256, 6.0)]
    [InlineData(32, 3.008, 11.008, 8.0)]
    [InlineData(48, 4.512, 16.512, 12.0)]
    public void DerivedSizesFollowTheRatios(int size, double thickness, double fontSize, double gap)
    {
        Assert.Equal(thickness, RingGeometry.Thickness(size), 3);
        Assert.Equal(fontSize, RingGeometry.FontSize(size), 3);
        Assert.Equal(gap, RingGeometry.Gap(size), 3);
    }

    [Fact]
    public void CriticalFromTheApiIsAlwaysRed()
    {
        Assert.Equal(RingGeometry.Red, RingGeometry.ColorFor(2, Severity.Critical, 75));
    }

    [Fact]
    public void AtOrAboveTheThresholdIsAmber()
    {
        Assert.Equal(RingGeometry.Amber, RingGeometry.ColorFor(75, Severity.Normal, 75));
        Assert.Equal(RingGeometry.Amber, RingGeometry.ColorFor(99, Severity.Normal, 75));
    }

    [Fact]
    public void BelowTheThresholdIsGreen()
    {
        Assert.Equal(RingGeometry.Green, RingGeometry.ColorFor(74.9, Severity.Normal, 75));
    }

    [Fact]
    public void PercentsAreWholeNumbersWithoutASign()
    {
        Assert.Equal("90", RingGeometry.FormatPercent(90.4));
        Assert.Equal("100", RingGeometry.FormatPercent(100));
        Assert.Equal("0", RingGeometry.FormatPercent(0.2));
    }

    [Theory]
    [InlineData(134, "2h 14m")]
    [InlineData(14, "14m")]
    [InlineData(0, "now")]
    public void ResetTimeIsShortAndHumanReadable(int minutes, string expected)
    {
        Assert.Equal(expected, RingGeometry.FormatResetIn(TimeSpan.FromMinutes(minutes)));
    }
}
