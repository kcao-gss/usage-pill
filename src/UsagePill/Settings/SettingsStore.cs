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
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalized(), Options));
        File.Move(temp, _path, overwrite: true);
    }
}
