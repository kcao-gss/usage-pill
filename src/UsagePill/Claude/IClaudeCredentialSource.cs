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
}
