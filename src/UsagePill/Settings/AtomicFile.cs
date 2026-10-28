using System.IO;

namespace UsagePill.Settings;

/// <summary>Writes a file so a reader only ever sees the old contents or the new, never half of either.</summary>
public static class AtomicFile
{
    /// <summary>A temp file this old cannot belong to a save still in flight.</summary>
    private static readonly TimeSpan OrphanAge = TimeSpan.FromMinutes(5);

    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        SweepOrphanedTempFiles(path, directory);

        // A unique name keeps concurrent saves off each other's temp file.
        var temp = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// A save killed mid-flight leaves its uniquely named temp file behind for good, so drop
    /// any sibling temp file old enough that no live save can still own it. Best effort only.
    /// </summary>
    private static void SweepOrphanedTempFiles(string path, string? directory)
    {
        var cutoff = DateTime.UtcNow - OrphanAge;
        try
        {
            var folder = string.IsNullOrEmpty(directory) ? "." : directory;
            foreach (var orphan in Directory.EnumerateFiles(folder, Path.GetFileName(path) + ".*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(orphan) < cutoff) TryDelete(orphan);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Sweeping is housekeeping: never let it fail a save.
        }
    }

    /// <summary>Deletes a temp file without letting the cleanup mask the real failure.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file another process holds open stays; the sweep picks it up later.
        }
    }
}
