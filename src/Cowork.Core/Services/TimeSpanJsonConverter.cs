using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cowork.Core.Services;

/// <summary>
/// Ghi TimeSpan dưới dạng "HH:mm:ss" cho dễ đọc/sửa tay trong workspace.json,
/// thay vì phụ thuộc vào hành vi mặc định của System.Text.Json.
/// </summary>
public sealed class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return TimeSpan.FromMinutes(reader.GetDouble());

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return TimeSpan.Zero;

        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? value
            : TimeSpan.Zero;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
}

/// <summary>Biến thể cho phép giá trị null.</summary>
public sealed class NullableTimeSpanJsonConverter : JsonConverter<TimeSpan?>
{
    private static readonly TimeSpanJsonConverter Inner = new();

    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeof(TimeSpan), options);

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            Inner.Write(writer, value.Value, options);
    }
}
