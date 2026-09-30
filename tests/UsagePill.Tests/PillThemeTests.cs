using System.Windows.Media;
using UsagePill.Settings;
using UsagePill.Ui;

namespace UsagePill.Tests;

public class PillThemeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AmoledIsPitchBlackWhateverWindowsUses(bool windowsIsDark)
    {
        var theme = PillTheme.For(BackgroundTheme.Amoled, windowsIsDark);

        Assert.Equal(Colors.Black, theme.CapsuleBackground);
        Assert.Equal(Colors.Black, theme.RingCore);
        Assert.Equal(Colors.Black, theme.CardBackground);
    }

    [Fact]
    public void SystemFollowsTheWindowsTheme()
    {
        Assert.Equal(PillTheme.For(BackgroundTheme.Dark, windowsIsDark: false), PillTheme.For(BackgroundTheme.System, windowsIsDark: true));
        Assert.Equal(PillTheme.For(BackgroundTheme.Light, windowsIsDark: true), PillTheme.For(BackgroundTheme.System, windowsIsDark: false));
    }

    [Fact]
    public void AnExplicitChoiceIgnoresTheWindowsTheme()
    {
        Assert.Equal(PillTheme.For(BackgroundTheme.Dark, windowsIsDark: true), PillTheme.For(BackgroundTheme.Dark, windowsIsDark: false));
        Assert.Equal(PillTheme.For(BackgroundTheme.Light, windowsIsDark: true), PillTheme.For(BackgroundTheme.Light, windowsIsDark: false));
        Assert.NotEqual(PillTheme.For(BackgroundTheme.Dark, windowsIsDark: true), PillTheme.For(BackgroundTheme.Light, windowsIsDark: true));
    }
}
