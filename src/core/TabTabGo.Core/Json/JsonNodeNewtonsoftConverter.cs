using Nj = Newtonsoft.Json;
using NjLinq = Newtonsoft.Json.Linq;

namespace TabTabGo.Core.Json;

/// <summary>
/// Lets Newtonsoft.Json read and write the <see cref="JsonNode"/> family (JsonNode, JsonObject, JsonArray, JsonValue).
/// </summary>
public sealed class JsonNodeNewtonsoftConverter : Nj.JsonConverter
{
    /// <inheritdoc />
    public override bool CanConvert(Type objectType) => typeof(JsonNode).IsAssignableFrom(objectType);

    /// <inheritdoc />
    public override void WriteJson(Nj.JsonWriter writer, object? value, Nj.JsonSerializer serializer)
    {
        if (value is not JsonNode node)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteRawValue(node.ToJsonString());
    }

    /// <inheritdoc />
    public override object? ReadJson(Nj.JsonReader reader, Type objectType, object? existingValue, Nj.JsonSerializer serializer)
    {
        if (reader.TokenType == Nj.JsonToken.Null)
        {
            return null;
        }

        var token = NjLinq.JToken.Load(reader);
        var node = token.ToJsonNode();
        if (node is null || objectType.IsInstanceOfType(node))
        {
            return node;
        }

        throw new Nj.JsonSerializationException($"Cannot convert JSON {token.Type} to {objectType.Name}.");
    }
}
