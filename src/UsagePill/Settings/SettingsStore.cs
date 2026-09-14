using System.IO;
using System.Text.Json;

namespace UsagePill.Settings;

/// <summary>Loads and saves settings as JSON under %APPDATA%\usage-pill.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>A temp file this old cannot belong to a save still in flight.</summary>
    private static readonly TimeSpan OrphanAge = TimeSpan.FromMinutes(5);

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "usage-pill",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(_path);
            return (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings()).Normalized();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // A missing or damaged file is not an error: fall back to the defaults.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        SweepOrphanedTempFiles(directory);

        // A unique name keeps concurrent saves off each other's temp file.
        var temp = _path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalized(), Options));
            File.Move(temp, _path, overwrite: true);
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
    private void SweepOrphanedTempFiles(string? directory)
    {
        var cutoff = DateTime.UtcNow - OrphanAge;
        try
        {
            var folder = string.IsNullOrEmpty(directory) ? "." : directory;
            foreach (var orphan in Directory.EnumerateFiles(folder, Path.GetFileName(_path) + ".*.tmp"))
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
