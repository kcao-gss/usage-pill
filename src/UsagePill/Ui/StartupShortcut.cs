using System.IO;
using System.Runtime.InteropServices;

namespace UsagePill.Ui;

/// <summary>
/// Adds or removes a real shortcut to the app executable in the user Startup folder, so Windows
/// launches the app silently at logon. Creates the .lnk through the Windows Script Host's
/// WScript.Shell COM object via late binding (the "dynamic" keyword), which needs no package
/// reference beyond the in-box Microsoft.CSharp assembly. A batch launcher would run through
/// cmd.exe and flash a console window at every login, so it is not used here.
/// </summary>
public static class StartupShortcut
{
    private const string LinkFileName = "usage-pill.lnk";

    // An earlier build wrote a .cmd launcher instead of a shortcut. Set(...) always removes it
    // so a user who upgrades is never left with both a stray .cmd and a .lnk.
    private const string LegacyCmdFileName = "usage-pill.cmd";

    private static string StartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    private static string LinkPath => Path.Combine(StartupFolder, LinkFileName);
    private static string LegacyCmdPath => Path.Combine(StartupFolder, LegacyCmdFileName);

    public static bool IsEnabled() => File.Exists(LinkPath);

    public static void Set(bool enabled)
    {
        if (File.Exists(LegacyCmdPath)) File.Delete(LegacyCmdPath);

        if (!enabled)
        {
            if (File.Exists(LinkPath)) File.Delete(LinkPath);
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the executable path.");
        CreateShortcut(LinkPath, exe);
    }

    private static void CreateShortcut(string linkPath, string exePath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not available.");

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(linkPath);
            try
            {
                link.TargetPath = exePath;
                link.WorkingDirectory = Path.GetDirectoryName(exePath);
                link.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
