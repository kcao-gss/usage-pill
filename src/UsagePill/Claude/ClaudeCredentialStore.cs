using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>
/// Reads one Claude Code credentials file, and writes a refreshed token back into it.
///
/// Claude Code owns the file and refreshes the token in place, and so can other tools such
/// as the Raycast Agent Usage extension. A refresh can replace the refresh token, which makes
/// the old one invalid, so the write is guarded: it changes the file only while the file
/// still holds the tokens the refresh started from. If someone else refreshed in the
/// meantime, their tokens stay. The write is in place, so the file keeps its owner, ACL and
/// WSL mode, and every field other than the three token fields is kept.
/// </summary>
public sealed class ClaudeCredentialStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        // Node writes the file without HTML escaping; keep URLs in other fields byte-identical.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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

        return Parse(content);
    }

    /// <summary>
    /// Exchanges the refresh token in <paramref name="current"/> through
    /// <paramref name="exchange"/> and writes the new tokens into the file. The file is checked
    /// before the exchange and again before the write: when it no longer holds
    /// <paramref name="current"/>, another program refreshed it, and the file is left alone.
    /// Read the file again afterwards to get whichever token it holds.
    /// </summary>
    /// <exception cref="AuthExpiredException">The file cannot be opened for writing, so a refresh would be lost.</exception>
    public async Task RefreshAsync(
        ClaudeCredentials current,
        Func<string, CancellationToken, Task<ClaudeTokenGrant>> exchange,
        CancellationToken ct)
    {
        var refreshToken = current.RefreshToken
            ?? throw new ArgumentException("The credentials hold no refresh token.", nameof(current));

        // A refresh can make the old refresh token invalid. Prove the file is writable before
        // spending it, so a new token pair is never received with nowhere to keep it.
        try
        {
            using var stream = Open(FileShare.ReadWrite | FileShare.Delete);
            if (!Holds(ReadAll(stream), current)) return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new AuthExpiredException($"Cannot write {_path}, so the expired token cannot be refreshed: {e.Message}");
        }

        var grant = await exchange(refreshToken, ct).ConfigureAwait(false);

        // Block other writers between the check and the write. Readers may continue.
        using (var stream = Open(FileShare.Read))
        {
            var content = ReadAll(stream);
            if (!Holds(content, current)) return;

            var root = JsonNode.Parse(content)!.AsObject();
            var oauth = root["claudeAiOauth"]!.AsObject();
            oauth["accessToken"] = grant.AccessToken;
            if (grant.RefreshToken is { } rotated) oauth["refreshToken"] = rotated;
            oauth["expiresAt"] = grant.ExpiresAt.ToUnixTimeMilliseconds();

            // One write of the whole content, so a reader never sees a token pair half replaced.
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(root.ToJsonString(WriteOptions));
            stream.SetLength(0);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
    }

    private FileStream Open(FileShare share) =>
        new(_path, FileMode.Open, FileAccess.ReadWrite, share);

    private static string ReadAll(FileStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    /// <summary>True when the file content still carries both tokens of <paramref name="current"/>.</summary>
    private static bool Holds(string content, ClaudeCredentials current)
    {
        try
        {
            var stored = Parse(content);
            return stored.AccessToken == current.AccessToken && stored.RefreshToken == current.RefreshToken;
        }
        catch (NoCredentialsException)
        {
            return false;
        }
    }

    private static ClaudeCredentials Parse(string content)
    {
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

            var accessToken = token.GetString()!;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                // The endpoint rejects a blank token outright, so it must not compete with real
                // ones: an undated blank token would otherwise outrank an expired real token.
                throw new NoCredentialsException("The claudeAiOauth.accessToken in the credentials file is empty.");
            }

            return new ClaudeCredentials(accessToken, ReadExpiry(oauth), ReadHasProfileScope(oauth), ReadRefreshToken(oauth));
        }
        catch (JsonException e)
        {
            throw new NoCredentialsException($"The credentials file is not valid JSON: {e.Message}");
        }
    }

    /// <summary>A missing, non-string or blank refresh token counts as none.</summary>
    private static string? ReadRefreshToken(JsonElement oauth)
    {
        if (!oauth.TryGetProperty("refreshToken", out var refreshToken) ||
            refreshToken.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = refreshToken.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// The usage endpoint requires the user:profile scope. Only a scopes array that lacks it
    /// counts as missing: a file without one leaves the decision to the endpoint.
    /// </summary>
    private static bool ReadHasProfileScope(JsonElement oauth)
    {
        if (!oauth.TryGetProperty("scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.ValueKind == JsonValueKind.String && scope.ValueEquals(ClaudeCredentials.ProfileScope)) return true;
        }

        return false;
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
