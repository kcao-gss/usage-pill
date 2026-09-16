using System.IO;
using System.Text.Json;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>
/// Reads one Claude Code credentials file. This type NEVER writes: Claude Code owns
/// the file and refreshes the token in place.
/// </summary>
public sealed class ClaudeCredentialStore
{
    private readonly string _path;

    public ClaudeCredentialStore(string path) => _path = path;

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
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                oauth.ValueKind != JsonValueKind.Object ||
                !oauth.TryGetProperty("accessToken", out var token) ||
                token.ValueKind != JsonValueKind.String)
            {
                throw new NoCredentialsException("No claudeAiOauth.accessToken in the credentials file.");
            }

            return new ClaudeCredentials(token.GetString()!, ReadExpiry(oauth));
        }
        catch (JsonException e)
        {
            throw new NoCredentialsException($"The credentials file is not valid JSON: {e.Message}");
        }
    }

    /// <summary>
    /// claudeAiOauth.expiresAt is milliseconds since the Unix epoch. A missing, non-numeric
    /// or out-of-range value yields null rather than throwing: the token itself is still
    /// usable, it only loses the comparison against a source that reports a real expiry.
    /// </summary>
    private static DateTimeOffset? ReadExpiry(JsonElement oauth)
    {
        if (!oauth.TryGetProperty("expiresAt", out var expiresAt) ||
            expiresAt.ValueKind != JsonValueKind.Number ||
            !expiresAt.TryGetInt64(out var epochMs))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
