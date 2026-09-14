using System.IO;

namespace UsagePill.Ui;

/// <summary>
/// Adds or removes a .cmd launcher in the user Startup folder. A script avoids the
/// COM interop a .lnk needs, and Windows runs it the same way.
/// </summary>
public static class StartupShortcut
{
    private static string Path_ => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "usage-pill.cmd");

    public static bool IsEnabled() => File.Exists(Path_);

    public static void Set(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(Path_)) File.Delete(Path_);
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the executable path.");
        File.WriteAllText(Path_, $"@echo off\r\nstart \"\" \"{exe}\"\r\n");
    }
}
