using System.Globalization;

namespace TabTabGo.Core.Converters;

/// <summary>
///  JsonConvertor for TimeSpan: written as a JSON number of seconds; read from a number or a numeric string.
/// </summary>
public class TimespanToSecondsConverter : JsonConverter<TimeSpan>
{
    /// <inheritdoc />
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double totalSeconds;
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out totalSeconds))
            {
                throw new JsonException($"'{text}' is not a number of seconds.");
            }
        }
        else
        {
            totalSeconds = reader.GetDouble();
        }

        return TimeSpan.FromSeconds(totalSeconds);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.TotalSeconds);
    }
}
