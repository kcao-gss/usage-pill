using System.IO;
using UsagePill.Claude;
using UsagePill.Core;

namespace UsagePill.Tests;

public class ClaudeCredentialResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("usagepill").FullName;

    /// <summary>Writes a credentials file the way Claude Code does, on Windows or in WSL.</summary>
    private string WriteCredentials(string name, string token, DateTimeOffset? expiresAt)
    {
        var path = Path.Combine(_dir, name);
        var expiry = expiresAt is null ? "" : $""" , "expiresAt": {expiresAt.Value.ToUnixTimeMilliseconds()} """;
        File.WriteAllText(path, $$"""{ "claudeAiOauth": { "accessToken": "{{token}}"{{expiry}} } }""");
        return path;
    }

    private string WriteRaw(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static ClaudeCredentialResolver Resolver(Func<IReadOnlyList<string>> candidates) => new(candidates);

    private static ClaudeCredentialResolver Resolver(params string[] candidates) => new(() => candidates);

    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UsesTheTokenWithTheLatestExpiry()
    {
        var windows = WriteCredentials("windows.json", "tok-windows", Noon);
        var wsl = WriteCredentials("wsl.json", "tok-wsl", Noon.AddHours(3));

        var credentials = Resolver(windows, wsl).Read();

        Assert.Equal("tok-wsl", credentials.AccessToken);
    }

    [Fact]
    public void PrefersAnyExpiryOverNone()
    {
        var undated = WriteCredentials("undated.json", "tok-undated", null);
        var dated = WriteCredentials("dated.json", "tok-dated", Noon);

        Assert.Equal("tok-dated", Resolver(undated, dated).Read().AccessToken);
        Assert.Equal("tok-dated", Resolver(dated, undated).Read().AccessToken);
    }

    [Fact]
    public void SkipsMissingAndUnusableCandidates()
    {
        var absent = Path.Combine(_dir, "absent.json");
        var broken = WriteRaw("broken.json", "not json at all");
        var signedOut = WriteRaw("signed-out.json", """{ "claudeAiOauth": { } }""");
        var usable = WriteCredentials("usable.json", "tok-usable", Noon);

        var credentials = Resolver(absent, broken, signedOut, usable).Read();

        Assert.Equal("tok-usable", credentials.AccessToken);
    }

    [Fact]
    public void ReportsNoCredentialsWhenNoSourceIsSignedIn()
    {
        var resolver = Resolver(Path.Combine(_dir, "absent.json"), WriteRaw("broken.json", "{"));

        Assert.Throws<NoCredentialsException>(() => resolver.Read());
    }

    [Fact]
    public void PicksUpTheRefreshedTokenInTheChosenFileWithoutRescanning()
    {
        var windows = WriteCredentials("windows.json", "tok-old", Noon);
        var scans = 0;
        var resolver = Resolver(() => { scans++; return new[] { windows }; });

        Assert.Equal("tok-old", resolver.Read().AccessToken);
        WriteCredentials("windows.json", "tok-refreshed", Noon.AddHours(5));

        Assert.Equal("tok-refreshed", resolver.Read().AccessToken);
        Assert.Equal(1, scans);
    }

    [Fact]
    public void RescansWhenTheChosenFileStopsBeingReadable()
    {
        var windows = WriteCredentials("windows.json", "tok-windows", Noon.AddHours(3));
        var wsl = WriteCredentials("wsl.json", "tok-wsl", Noon);
        var resolver = Resolver(windows, wsl);

        Assert.Equal("tok-windows", resolver.Read().AccessToken);
        File.Delete(windows);

        Assert.Equal("tok-wsl", resolver.Read().AccessToken);
    }

    [Fact]
    public void RescansAfterInvalidateSoAFresherSourceCanTakeOver()
    {
        var windows = WriteCredentials("windows.json", "tok-windows", Noon.AddHours(3));
        var wsl = WriteCredentials("wsl.json", "tok-wsl", Noon);
        var resolver = Resolver(windows, wsl);

        Assert.Equal("tok-windows", resolver.Read().AccessToken);

        // The endpoint rejected the Windows token, and meanwhile Claude Code in WSL signed in.
        resolver.Invalidate();
        WriteCredentials("wsl.json", "tok-wsl-fresh", Noon.AddHours(9));

        Assert.Equal("tok-wsl-fresh", resolver.Read().AccessToken);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
