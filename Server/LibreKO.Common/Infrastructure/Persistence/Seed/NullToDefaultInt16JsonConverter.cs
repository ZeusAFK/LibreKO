using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public sealed class NullToDefaultInt16JsonConverter : JsonConverter<short>
{
    public override short Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var stringValue = reader.TokenType == JsonTokenType.String
            ? FlexibleJsonValueParser.ReadString(ref reader)
            : null;

        return reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.GetInt16(),
            JsonTokenType.String => FlexibleJsonValueParser.IsEmpty(stringValue)
                ? (short)0
                : short.Parse(stringValue!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for Int16 value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, short value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
