using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsagePill.Settings;

/// <summary>Reads an enum by name and falls back to the default on an unknown value.</summary>
public sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        if (reader.TokenType != JsonTokenType.String) reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
