
using TabTabGo.Core.Attributes;

namespace TabTabGo.Core.Converters;

public class JsonEnumConverter<T> : JsonConverter<T> where T : Enum
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException();
        }

        string? value = reader.GetString();
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = field.GetCustomAttribute<JsonEnumNameAttribute>();
            if (attribute != null && attribute.Name == value)
            {
                return (T)field.GetValue(null)!;
            }
        }

        throw new JsonException($"Unknown enum value: {value}");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var field = typeof(T).GetField(value.ToString());
        var attribute = field.GetCustomAttribute<JsonEnumNameAttribute>();
        if (attribute != null)
        {
            writer.WriteStringValue(attribute.Name);
        }
        else
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}