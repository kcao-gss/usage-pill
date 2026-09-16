using System.IO;

namespace UsagePill.Claude;

/// <summary>
/// Every place Claude Code can keep its credentials file for this user: the Windows
/// profile, plus the root and per-user home directories of each WSL distribution,
/// reached over the WSL file share.
/// </summary>
public static class CredentialSources
{
    private const string ClaudeDirectoryName = ".claude";
    private const string CredentialsFileName = ".credentials.json";

    /// <summary>Windows 10 21H2 and later expose the share here; older builds only answer on \\wsl$.</summary>
    private static readonly string[] SharePrefixes = [@"\\wsl.localhost\", @"\\wsl$\"];

    public static string WindowsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ClaudeDirectoryName,
        CredentialsFileName);

    /// <summary>
    /// Enumerated fresh on every scan, so a distribution installed while the pill runs is
    /// found without a restart. Touching the share starts a stopped distribution, which is
    /// why the resolver scans rarely and re-reads its chosen file the rest of the time.
    /// </summary>
    public static IReadOnlyList<string> All()
    {
        var paths = new List<string> { WindowsPath() };

        foreach (var distribution in WslDistributions.Names())
        {
            var root = ShareRoot(distribution);
            if (root is null) continue;

            paths.Add(Path.Combine(root, "root", ClaudeDirectoryName, CredentialsFileName));
            foreach (var home in HomeDirectories(root))
            {
                paths.Add(Path.Combine(home, ClaudeDirectoryName, CredentialsFileName));
            }
        }

        return paths;
    }

    private static string? ShareRoot(string distribution)
    {
        foreach (var prefix in SharePrefixes)
        {
            var root = prefix + distribution;
            if (Directory.Exists(root)) return root;
        }

        return null;
    }

    private static IReadOnlyList<string> HomeDirectories(string root)
    {
        try
        {
            return Directory.GetDirectories(Path.Combine(root, "home"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A distribution that refuses to start, or one without /home, contributes nothing.
            return Array.Empty<string>();
        }
    }
}
