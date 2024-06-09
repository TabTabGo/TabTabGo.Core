namespace TabTabGo.Core.Converters;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
///  JsonConvertor for TimeSpan
/// </summary>
public class TimespanToSecondsConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // create timespan from total minutes
        var totalSeconds = reader.GetDouble();
        var timespan = TimeSpan.FromSeconds(totalSeconds);
        return timespan;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.TotalSeconds.ToString(CultureInfo.CurrentCulture));
    }
}