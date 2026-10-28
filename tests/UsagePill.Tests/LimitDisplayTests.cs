using UsagePill.Pacing;
using UsagePill.Ui;

namespace UsagePill.Tests;

public class LimitDisplayTests
{
    [Theory]
    [InlineData(31.67, 12.4, "$19")]   // left rounds down, never promising money that is not there
    [InlineData(31.67, 39.2, "+$8")]   // over rounds up, never hiding overspend
    [InlineData(32, 32, "$0")]
    public void MoneyLeftRoundsTowardCaution(double share, double spent, string expected)
    {
        Assert.Equal(expected, LimitDisplay.FormatLeft(new SpendPace((decimal)spent, (decimal)share), "USD"));
    }

    [Fact]
    public void OtherCurrenciesUseTheirCode()
    {
        Assert.Equal("EUR 19", LimitDisplay.FormatLeft(new SpendPace(12.4m, 31.67m), "EUR"));
    }
}
