using Nj = Newtonsoft.Json;
using NjLinq = Newtonsoft.Json.Linq;

namespace TabTabGo.Core.Json;

/// <summary>
/// Lets Newtonsoft.Json read and write <see cref="JsonElement"/> and <see cref="JsonDocument"/>.
/// </summary>
public sealed class JsonElementNewtonsoftConverter : Nj.JsonConverter
{
    /// <inheritdoc />
    public override bool CanConvert(Type objectType) =>
        objectType == typeof(JsonElement) || objectType == typeof(JsonDocument);

    /// <inheritdoc />
    public override void WriteJson(Nj.JsonWriter writer, object? value, Nj.JsonSerializer serializer)
    {
        switch (value)
        {
            case JsonElement element when element.ValueKind != JsonValueKind.Undefined:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonDocument document:
                writer.WriteRawValue(document.RootElement.GetRawText());
                break;
            default:
                writer.WriteNull();
                break;
        }
    }

    /// <inheritdoc />
    public override object? ReadJson(Nj.JsonReader reader, Type objectType, object? existingValue, Nj.JsonSerializer serializer)
    {
        var token = reader.TokenType == Nj.JsonToken.Null ? NjLinq.JValue.CreateNull() : NjLinq.JToken.Load(reader);
        if (objectType == typeof(JsonDocument))
        {
            return token.Type == NjLinq.JTokenType.Null && reader.TokenType == Nj.JsonToken.Null ? null : token.ToJsonDocument();
        }

        return token.ToJsonElement();
    }
}
