using Microsoft.Win32;

namespace UsagePill.Claude;

/// <summary>
/// Lists the WSL distributions registered for the current user, straight from the
/// registry key the WSL service itself keeps. Reading the registry never starts a
/// distribution, which running <c>wsl.exe -l</c> risks doing.
/// </summary>
public static class WslDistributions
{
    private const string LxssKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    /// <summary>Docker ships two internal distributions that never hold a Claude login.</summary>
    private const string DockerPrefix = "docker-desktop";

    public static IReadOnlyList<string> Names()
    {
        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(LxssKeyPath);
            if (lxss is null) return Array.Empty<string>();

            var names = new List<string>();
            foreach (var subKeyName in lxss.GetSubKeyNames())
            {
                using var distro = lxss.OpenSubKey(subKeyName);
                if (distro?.GetValue("DistributionName") is not string name) continue;
                if (name.Length == 0) continue;
                if (name.StartsWith(DockerPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                names.Add(name);
            }

            return names;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // A locked-down profile hides the key. WSL sources are then simply unavailable;
            // the Windows credentials file still works.
            return Array.Empty<string>();
        }
    }
}
