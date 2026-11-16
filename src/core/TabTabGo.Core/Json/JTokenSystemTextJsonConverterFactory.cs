using Nj = Newtonsoft.Json;
using NjLinq = Newtonsoft.Json.Linq;

namespace TabTabGo.Core.Json;

/// <summary>
/// Lets System.Text.Json read and write Newtonsoft <see cref="NjLinq.JToken"/> trees (JObject, JArray, JValue).
/// Without it System.Text.Json writes a JToken as nested empty arrays.
/// </summary>
public sealed class JTokenSystemTextJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeof(NjLinq.JToken).IsAssignableFrom(typeToConvert);

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(JTokenConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    private sealed class JTokenConverter<T> : JsonConverter<T> where T : NjLinq.JToken
    {
        public override bool HandleNull => true;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var token = document.RootElement.ToJToken();
            if (token is T typed)
            {
                return typed;
            }

            if (token.Type == NjLinq.JTokenType.Null && !typeof(NjLinq.JValue).IsAssignableFrom(typeof(T)) && typeToConvert != typeof(NjLinq.JToken))
            {
                return null;
            }

            throw new JsonException($"Cannot convert JSON {document.RootElement.ValueKind} to {typeToConvert.Name}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            JTokenWriter.Write(writer, value);
    }
}
