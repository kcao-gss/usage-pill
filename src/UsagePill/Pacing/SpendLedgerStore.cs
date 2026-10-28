using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using UsagePill.Settings;

namespace UsagePill.Pacing;

/// <summary>Loads and saves the spend ledger as JSON next to settings.json (spec section 5).</summary>
public sealed class SpendLedgerStore
{
    private const string MonthFormat = "yyyy-MM";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;

    public SpendLedgerStore(string path) => _path = path;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "usage-pill",
        "spend-ledger.json");

    public SpendLedger Load()
    {
        try
        {
            var file = JsonSerializer.Deserialize<LedgerFile>(File.ReadAllText(_path), Options);
            if (file?.Month is null || file.Currency is null || file.Days is null) return SpendLedger.Empty;

            var month = DateTime.ParseExact(file.Month, MonthFormat, CultureInfo.InvariantCulture);
            var days = file.Days.ToImmutableSortedDictionary(
                pair => DateOnly.ParseExact(pair.Key, DateFormat, CultureInfo.InvariantCulture),
                pair => new SpendDayReading(pair.Value.First, pair.Value.Last));
            return new SpendLedger(new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero), file.Currency, days);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or FormatException)
        {
            // A missing or damaged file is not an error: the ledger starts over, as settings do.
            return SpendLedger.Empty;
        }
    }

    public void Save(SpendLedger ledger)
    {
        var file = new LedgerFile(
            ledger.MonthStart.UtcDateTime.ToString(MonthFormat, CultureInfo.InvariantCulture),
            ledger.Currency,
            ledger.Days.ToDictionary(
                pair => pair.Key.ToString(DateFormat, CultureInfo.InvariantCulture),
                pair => new DayEntry(pair.Value.First, pair.Value.Last)));
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, Options));
    }

    private sealed record LedgerFile(string? Month, string? Currency, Dictionary<string, DayEntry>? Days);

    private sealed record DayEntry(decimal First, decimal Last);
}
