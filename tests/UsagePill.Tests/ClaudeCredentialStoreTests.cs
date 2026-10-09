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
    public void ReadsTheAccessToken()
    {
        var path = WriteFile("""
        { "mcpOAuth": {}, "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-abc", "refreshToken": "sk-ant-ort01-def",
            "expiresAt": 1789423372964, "subscriptionType": "team" } }
        """);

        var credentials = new ClaudeCredentialStore(path).Read();

        Assert.Equal("sk-ant-oat01-abc", credentials.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1789423372964), credentials.ExpiresAt);
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ThrowsNoCredentialsWhenTheAccessTokenIsBlank(string token)
    {
        var store = new ClaudeCredentialStore(WriteFile($$"""{ "claudeAiOauth": { "accessToken": "{{token}}" } }"""));

        Assert.Throws<NoCredentialsException>(() => store.Read());
    }

    [Theory]
    [InlineData("""["user:inference", "user:profile"]""", true)]
    [InlineData("""["user:inference"]""", false)]
    [InlineData("""[]""", false)]
    [InlineData(null, true)]
    public void ReportsWhetherTheTokenCarriesTheProfileScope(string? scopes, bool expected)
    {
        var scopesProperty = scopes is null ? "" : $""" , "scopes": {scopes} """;
        var path = WriteFile($$"""{ "claudeAiOauth": { "accessToken": "sk-ant-oat01-abc"{{scopesProperty}} } }""");

        Assert.Equal(expected, new ClaudeCredentialStore(path).Read().HasProfileScope);
    }

    [Fact]
    public void ReadsTheAccessTokenWhenExpiresAtIsUnparsablyLarge()
    {
        // A truncated or garbled numeric expiresAt (here, one far outside the range
        // DateTimeOffset.FromUnixTimeMilliseconds accepts) must not throw
        // ArgumentOutOfRangeException out of Read(): the token is still usable, it only
        // loses the freshness comparison against the other credential sources.
        var path = WriteFile("""
        { "claudeAiOauth": { "accessToken": "sk-ant-oat01-abc", "expiresAt": 9999999999999999 } }
        """);

        var credentials = new ClaudeCredentialStore(path).Read();

        Assert.Equal("sk-ant-oat01-abc", credentials.AccessToken);
        Assert.Null(credentials.ExpiresAt);
    }

    [Fact]
    public void ReportsNoExpiryWhenExpiresAtIsAbsent()
    {
        var path = WriteFile("""{ "claudeAiOauth": { "accessToken": "sk-ant-oat01-abc" } }""");

        var credentials = new ClaudeCredentialStore(path).Read();

        Assert.Null(credentials.ExpiresAt);
    }

    [Fact]
    public void NeverPrintsATokenOrARefreshToken()
    {
        var credentials = new ClaudeCredentials("sk-ant-oat01-abc", DateTimeOffset.UnixEpoch, RefreshToken: "sk-ant-ort01-def");
        var grant = new ClaudeTokenGrant("sk-ant-oat01-new", "sk-ant-ort01-new", DateTimeOffset.UnixEpoch);

        var text = credentials.ToString() + grant.ToString();

        Assert.DoesNotContain("sk-ant-oat01-abc", text);
        Assert.DoesNotContain("sk-ant-ort01-def", text);
        Assert.DoesNotContain("sk-ant-oat01-new", text);
        Assert.DoesNotContain("sk-ant-ort01-new", text);
    }

    [Fact]
    public void ReadsTheRefreshTokenAndTreatsABlankOneAsNone()
    {
        var path = WriteFile(SignedIn);
        Assert.Equal("ref-old", new ClaudeCredentialStore(path).Read().RefreshToken);

        WriteFile("""{ "claudeAiOauth": { "accessToken": "tok-old", "refreshToken": "  " } }""");
        Assert.Null(new ClaudeCredentialStore(path).Read().RefreshToken);
    }

    private const string SignedIn = """
        { "mcpOAuth": { "server|1": { "serverUrl": "https://example.com/mcp?a=1&b=2" } },
          "claudeAiOauth": { "accessToken": "tok-old", "refreshToken": "ref-old", "expiresAt": 1000,
            "scopes": ["user:inference", "user:profile"], "subscriptionType": "team" } }
        """;

    private static readonly DateTimeOffset NewExpiry = DateTimeOffset.FromUnixTimeMilliseconds(1791586556353);

    [Fact]
    public async Task RefreshWritesTheNewTokensAndKeepsEveryOtherField()
    {
        var path = WriteFile(SignedIn);
        var store = new ClaudeCredentialStore(path);
        string? spent = null;

        await store.RefreshAsync(store.Read(), (refreshToken, _) =>
        {
            spent = refreshToken;
            return Task.FromResult(new ClaudeTokenGrant("tok-new", "ref-new", NewExpiry));
        }, CancellationToken.None);

        Assert.Equal("ref-old", spent);
        Assert.Equal(new ClaudeCredentials("tok-new", NewExpiry, true, "ref-new"), store.Read());

        var json = File.ReadAllText(path);
        Assert.Contains("\"subscriptionType\":\"team\"", json);
        Assert.Contains("\"scopes\":[\"user:inference\",\"user:profile\"]", json);
        Assert.Contains("https://example.com/mcp?a=1&b=2", json);
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "no BOM");
    }

    [Fact]
    public async Task RefreshKeepsTheRefreshTokenWhenTheGrantCarriesNone()
    {
        var path = WriteFile(SignedIn);
        var store = new ClaudeCredentialStore(path);

        await store.RefreshAsync(store.Read(),
            (_, _) => Task.FromResult(new ClaudeTokenGrant("tok-new", null, NewExpiry)), CancellationToken.None);

        Assert.Equal("ref-old", store.Read().RefreshToken);
        Assert.Equal("tok-new", store.Read().AccessToken);
    }

    [Fact]
    public async Task RefreshLeavesTokensAnotherProgramWroteDuringTheExchange()
    {
        var path = WriteFile(SignedIn);
        var store = new ClaudeCredentialStore(path);
        const string Raycast = """{ "claudeAiOauth": { "accessToken": "tok-raycast", "refreshToken": "ref-raycast" } }""";

        await store.RefreshAsync(store.Read(), (_, _) =>
        {
            // Raycast or Claude Code refreshed the same file while the request was in flight.
            File.WriteAllText(path, Raycast);
            return Task.FromResult(new ClaudeTokenGrant("tok-new", "ref-new", NewExpiry));
        }, CancellationToken.None);

        Assert.Equal(Raycast, File.ReadAllText(path));
    }

    [Fact]
    public async Task RefreshDoesNotSpendTheRefreshTokenOnceTheFileHoldsAnother()
    {
        var path = WriteFile(SignedIn);
        var store = new ClaudeCredentialStore(path);
        var stale = store.Read();
        WriteFile("""{ "claudeAiOauth": { "accessToken": "tok-claude", "refreshToken": "ref-claude" } }""");
        var exchanges = 0;

        await store.RefreshAsync(stale, (_, _) =>
        {
            exchanges++;
            return Task.FromResult(new ClaudeTokenGrant("tok-new", "ref-new", NewExpiry));
        }, CancellationToken.None);

        Assert.Equal(0, exchanges);
        Assert.Equal("tok-claude", store.Read().AccessToken);
    }

    [Fact]
    public async Task RefreshOfAFileItCannotWriteIsAnExpiredLoginWithoutAnExchange()
    {
        var path = WriteFile(SignedIn);
        var store = new ClaudeCredentialStore(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var exchanges = 0;

        try
        {
            await Assert.ThrowsAsync<AuthExpiredException>(() => store.RefreshAsync(store.Read(), (_, _) =>
            {
                exchanges++;
                return Task.FromResult(new ClaudeTokenGrant("tok-new", "ref-new", NewExpiry));
            }, CancellationToken.None));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Assert.Equal(0, exchanges);
        Assert.Equal("tok-old", store.Read().AccessToken);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
