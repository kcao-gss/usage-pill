namespace UsagePill.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProjectReferencesTheApp()
    {
        Assert.Equal("UsagePill", typeof(UsagePill.App).Assembly.GetName().Name);
    }
}
