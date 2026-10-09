namespace UsagePill.Claude;

/// <summary>Supplies the access token for each poll.</summary>
public interface IClaudeCredentialSource
{
    /// <summary>Throws <see cref="Core.NoCredentialsException"/> when no token is available.</summary>
    ClaudeCredentials Read();

    /// <summary>
    /// Tells the source the last token it returned was rejected, so the next read looks
    /// at every location again instead of trusting its remembered choice.
    /// </summary>
    void Invalidate();

    /// <summary>
    /// Renews <paramref name="current"/>, the expired token <see cref="Read"/> returned last, in
    /// the file it came from, and returns what that file holds afterwards: the refreshed token,
    /// or the token another program wrote there first. <paramref name="exchange"/> trades a
    /// refresh token for a new token pair.
    /// </summary>
    Task<ClaudeCredentials> RefreshAsync(
        ClaudeCredentials current,
        Func<string, CancellationToken, Task<ClaudeTokenGrant>> exchange,
        CancellationToken ct);
}
