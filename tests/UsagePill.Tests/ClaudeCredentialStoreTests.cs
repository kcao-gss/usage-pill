using System.IO;
using UsagePill.Claude;
using UsagePill.Core;

namespace UsagePill.Tests;

public class ClaudeCredentialStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("usagepill").FullName;

    private string WriteFile(string content)
    {
        var path = Path.Combine(_dir, ".credentials.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadsTheAccessTokenAndExpiry()
    {
        var path = WriteFile("""
        { "mcpOAuth": {}, "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-abc", "refreshToken": "sk-ant-ort01-def",
            "expiresAt": 1789423372964, "subscriptionType": "team" } }
        """);

        var credentials = new ClaudeCredentialStore(path).Read();

        Assert.Equal("sk-ant-oat01-abc", credentials.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1789423372964), credentials.ExpiresAt);
        Assert.Equal("team", credentials.SubscriptionType);
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheFileIsMissing()
    {
        var store = new ClaudeCredentialStore(Path.Combine(_dir, "absent.json"));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheTokenIsAbsent()
    {
        var store = new ClaudeCredentialStore(WriteFile("""{ "claudeAiOauth": { "expiresAt": 1 } }"""));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheFileIsNotJson()
    {
        var store = new ClaudeCredentialStore(WriteFile("not json at all"));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheFileIsEmpty()
    {
        var store = new ClaudeCredentialStore(WriteFile(""));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"x\"")]
    public void ThrowsNoCredentialsWhenTheRootIsNotAnObject(string json)
    {
        var store = new ClaudeCredentialStore(WriteFile(json));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheOauthNodeIsNotAnObject()
    {
        var store = new ClaudeCredentialStore(WriteFile("""{ "claudeAiOauth": "sk-ant-oat01-abc" }"""));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Fact]
    public void ThrowsNoCredentialsWhenTheAccessTokenIsNotAString()
    {
        var store = new ClaudeCredentialStore(WriteFile("""{ "claudeAiOauth": { "accessToken": 42 } }"""));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Fact]
    public void DegradesTheExpiryWhenItIsNotANumber()
    {
        var path = WriteFile("""
        { "claudeAiOauth": { "accessToken": "sk-ant-oat01-abc", "expiresAt": "soon" } }
        """);

        var credentials = new ClaudeCredentialStore(path).Read();

        Assert.Equal("sk-ant-oat01-abc", credentials.AccessToken);
        Assert.Equal(DateTimeOffset.MinValue, credentials.ExpiresAt);
    }

    [Fact]
    public void NeverPrintsTheAccessToken()
    {
        var credentials = new ClaudeCredentials("sk-ant-oat01-abc", DateTimeOffset.UnixEpoch, "team");

        var text = credentials.ToString();

        Assert.DoesNotContain("sk-ant-oat01-abc", text);
        Assert.Contains("team", text);
    }

    [Fact]
    public void DefaultPathPointsAtTheClaudeCredentialsFile()
    {
        var path = ClaudeCredentialStore.DefaultPath();

        Assert.EndsWith(Path.Combine(".claude", ".credentials.json"), path);
        Assert.True(Path.IsPathRooted(path));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
