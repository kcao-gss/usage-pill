using System.IO;
using System.Text.Json;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>
/// Reads the Claude Code credentials file. This type NEVER writes: Claude Code owns
/// the file and refreshes the token in place.
/// </summary>
public sealed class ClaudeCredentialStore
{
    private readonly string _path;

    public ClaudeCredentialStore(string path) => _path = path;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        ".credentials.json");

    public ClaudeCredentials Read()
    {
        string content;
        try
        {
            content = File.ReadAllText(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new NoCredentialsException($"Cannot read {_path}: {e.Message}");
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                oauth.ValueKind != JsonValueKind.Object ||
                !oauth.TryGetProperty("accessToken", out var token) ||
                token.ValueKind != JsonValueKind.String)
            {
                throw new NoCredentialsException("No claudeAiOauth.accessToken in the credentials file.");
            }

            var expiresAt = oauth.TryGetProperty("expiresAt", out var e) && e.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : DateTimeOffset.MinValue;

            var subscription = oauth.TryGetProperty("subscriptionType", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;

            return new ClaudeCredentials(token.GetString()!, expiresAt, subscription);
        }
        catch (JsonException e)
        {
            throw new NoCredentialsException($"The credentials file is not valid JSON: {e.Message}");
        }
    }
}
