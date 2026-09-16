using System.IO;
using UsagePill.Claude;

namespace UsagePill.Tests;

public class CredentialSourcesTests
{
    [Fact]
    public void TheWindowsPathPointsAtTheClaudeCredentialsFile()
    {
        var path = CredentialSources.WindowsPath();

        Assert.EndsWith(Path.Combine(".claude", ".credentials.json"), path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void TheWindowsProfileIsAlwaysACandidate()
    {
        // WSL sources depend on the machine, so this is all that can be asserted
        // everywhere. It still exercises the registry scan and the share probe.
        var paths = CredentialSources.All();

        Assert.Contains(CredentialSources.WindowsPath(), paths);
        Assert.All(paths, path => Assert.EndsWith(".credentials.json", path));
        Assert.Equal(paths.Distinct().Count(), paths.Count);
    }
}
