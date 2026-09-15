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
    public void TheRingColoursAreTheOnesTheSpecNames()
    {
        Assert.Equal(Color.FromRgb(0xF2, 0x55, 0x5A), RingGeometry.Red);
        Assert.Equal(Color.FromRgb(0xF5, 0xB7, 0x3D), RingGeometry.Amber);
        Assert.Equal(Color.FromRgb(0x3E, 0xCF, 0x8E), RingGeometry.Green);
        Assert.Equal(Color.FromRgb(0x6C, 0x76, 0x84), RingGeometry.Grey);
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
    [InlineData(1440, "1d 0h")]
    [InlineData(9792, "6d 19h")]
    public void ResetTimeIsShortAndHumanReadable(int minutes, string expected)
    {
        Assert.Equal(expected, RingGeometry.FormatResetIn(TimeSpan.FromMinutes(minutes)));
    }
}
