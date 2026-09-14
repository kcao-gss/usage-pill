using System.Globalization;
using System.Text.Json;
using UsagePill.Core;

namespace UsagePill.Claude;

/// <summary>Maps the /api/oauth/usage response onto the core model.</summary>
public static class ClaudeUsageJson
{
    public static UsageSnapshot Parse(string json, DateTimeOffset capturedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var limits = ReadLimitsArray(root);
        if (limits.Count == 0) limits = ReadLegacyShape(root);

        return new UsageSnapshot(limits, ReadSpend(root), capturedAt);
    }

    private static List<UsageLimit> ReadLimitsArray(JsonElement root)
    {
        var result = new List<UsageLimit>(4);
        if (!root.TryGetProperty("limits", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var kind = ParseKind(GetString(item, "kind"));
            if (kind == LimitKind.Unknown) continue;

            result.Add(new UsageLimit(
                kind,
                GetDouble(item, "percent") ?? 0,
                ParseSeverity(GetString(item, "severity")),
                GetTimestamp(item, "resets_at"),
                ReadScopeLabel(item)));
        }

        return result;
    }

    private static List<UsageLimit> ReadLegacyShape(JsonElement root)
    {
        var result = new List<UsageLimit>(2);
        AddLegacy(result, root, "five_hour", LimitKind.Session);
        AddLegacy(result, root, "seven_day", LimitKind.WeeklyAll);
        return result;
    }

    private static void AddLegacy(List<UsageLimit> into, JsonElement root, string property, LimitKind kind)
    {
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object) return;

        var utilization = GetDouble(node, "utilization");
        if (utilization is null) return;

        into.Add(new UsageLimit(kind, utilization.Value, Severity.Normal, GetTimestamp(node, "resets_at"), null));
    }

    private static SpendInfo? ReadSpend(JsonElement root)
    {
        if (!root.TryGetProperty("spend", out var spend) || spend.ValueKind != JsonValueKind.Object) return null;

        var used = ReadMoney(spend, "used");
        var limit = ReadMoney(spend, "limit");
        if (used is null || limit is null) return null;

        var currency = spend.TryGetProperty("limit", out var l) ? GetString(l, "currency") : null;
        return new SpendInfo(used.Value, limit.Value, currency ?? "USD");
    }

    private static decimal? ReadMoney(JsonElement spend, string property)
    {
        if (!spend.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object) return null;
        if (!node.TryGetProperty("amount_minor", out var minor) || !minor.TryGetDecimal(out var amount)) return null;

        var exponent = 2;
        if (node.TryGetProperty("exponent", out var e) && e.TryGetInt32(out var parsed)) exponent = parsed;

        var divisor = 1m;
        for (var i = 0; i < exponent; i++) divisor *= 10m;
        return amount / divisor;
    }

    private static string? ReadScopeLabel(JsonElement limit)
    {
        if (!limit.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object) return null;
        if (!scope.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object) return null;
        return GetString(model, "display_name");
    }

    private static LimitKind ParseKind(string? kind) => kind switch
    {
        "session" => LimitKind.Session,
        "weekly_all" => LimitKind.WeeklyAll,
        "weekly_scoped" => LimitKind.WeeklyScoped,
        _ => LimitKind.Unknown,
    };

    private static Severity ParseSeverity(string? severity) => severity switch
    {
        "critical" => Severity.Critical,
        "warning" => Severity.Warning,
        _ => Severity.Normal,
    };

    private static string? GetString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetDouble(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static DateTimeOffset? GetTimestamp(JsonElement node, string property)
    {
        var raw = GetString(node, property);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
