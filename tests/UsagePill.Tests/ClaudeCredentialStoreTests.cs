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

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
