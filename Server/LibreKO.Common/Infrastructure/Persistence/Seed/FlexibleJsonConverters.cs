using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibreKO.Common.Infrastructure.Persistence.Seed;

internal static class FlexibleJsonValueParser
{
    public static string? ReadString(ref Utf8JsonReader reader)
    {
        return reader.GetString()?.Trim();
    }

    public static bool IsEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FlexibleBooleanJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => false,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.TryGetInt64(out var value)
                ? value != 0
                : Math.Abs(reader.GetDouble()) > double.Epsilon,
            JsonTokenType.String => Parse(FlexibleJsonValueParser.ReadString(ref reader)),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for boolean value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }

    private static bool Parse(string? value)
    {
        if (FlexibleJsonValueParser.IsEmpty(value))
            return false;
        if (bool.TryParse(value, out var boolean))
            return boolean;
        if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var integer))
            return integer != 0;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return Math.Abs(number) > double.Epsilon;

        throw new JsonException($"Unable to convert '{value}' to boolean.");
    }
}

public sealed class FlexibleByteJsonConverter : JsonConverter<byte>
{
    public override byte Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.GetByte(),
            JsonTokenType.String => Parse(FlexibleJsonValueParser.ReadString(ref reader)),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for byte value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, byte value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static byte Parse(string? value)
    {
        if (FlexibleJsonValueParser.IsEmpty(value))
            return 0;

        return byte.Parse(value!, NumberStyles.Any, CultureInfo.InvariantCulture);
    }
}

public sealed class FlexibleSByteJsonConverter : JsonConverter<sbyte>
{
    public override sbyte Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.GetSByte(),
            JsonTokenType.String => Parse(FlexibleJsonValueParser.ReadString(ref reader)),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for sbyte value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, sbyte value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static sbyte Parse(string? value)
    {
        if (FlexibleJsonValueParser.IsEmpty(value))
            return 0;

        return sbyte.Parse(value!, NumberStyles.Any, CultureInfo.InvariantCulture);
    }
}

public sealed class FlexibleInt16JsonConverter : JsonConverter<short>
{
    public override short Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.GetInt16(),
            JsonTokenType.String => Parse(FlexibleJsonValueParser.ReadString(ref reader)),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for Int16 value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, short value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static short Parse(string? value)
    {
        if (FlexibleJsonValueParser.IsEmpty(value))
            return 0;

        return short.Parse(value!, NumberStyles.Any, CultureInfo.InvariantCulture);
    }
}

public sealed class FlexibleUInt16JsonConverter : JsonConverter<ushort>
{
    public override ushort Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.GetUInt16(),
            JsonTokenType.String => Parse(FlexibleJsonValueParser.ReadString(ref reader)),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for UInt16 value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ushort value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static ushort Parse(string? value)
    {
        if (FlexibleJsonValueParser.IsEmpty(value))
            return 0;

        return ushort.Parse(value!, NumberStyles.Any, CultureInfo.InvariantCulture);
    }
}

public sealed class FlexibleInt32JsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.GetInt32(),
            JsonTokenType.String => Parse(FlexibleJsonValueParser.ReadString(ref reader)),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for Int32 value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static int Parse(string? value)
    {
        if (FlexibleJsonValueParser.IsEmpty(value))
            return 0;

        return int.Parse(value!, NumberStyles.Any, CultureInfo.InvariantCulture);
    }
}

public sealed class FlexibleInt64JsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.GetInt64(),
            JsonTokenType.String => Parse(FlexibleJsonValueParser.ReadString(ref reader)),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for Int64 value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static long Parse(string? value)
    {
        if (FlexibleJsonValueParser.IsEmpty(value))
            return 0;

        return long.Parse(value!, NumberStyles.Any, CultureInfo.InvariantCulture);
    }
}

public sealed class FlexibleDoubleJsonConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => Parse(FlexibleJsonValueParser.ReadString(ref reader)),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for Double value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static double Parse(string? value)
    {
        if (FlexibleJsonValueParser.IsEmpty(value))
            return 0;

        return double.Parse(value!, NumberStyles.Any, CultureInfo.InvariantCulture);
    }
}
