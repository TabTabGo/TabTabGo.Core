using System.Collections.Concurrent;
using System.Globalization;
using TabTabGo.Core.Attributes;

namespace TabTabGo.Core.Converters;

/// <summary>
/// Reads and writes enums as strings, honouring <see cref="JsonEnumNameAttribute"/>. Reading accepts the attribute
/// name, the member name (case-insensitive) or a number; undefined values are written as numbers.
/// </summary>
/// <typeparam name="T">Enum type</typeparam>
public class JsonEnumConverter<T> : JsonConverter<T> where T : Enum
{
    private static readonly ConcurrentDictionary<string, T> ByName = BuildNames();
    private static readonly Dictionary<T, string> ToName = BuildReverse();

    private static ConcurrentDictionary<string, T> BuildNames()
    {
        var names = new ConcurrentDictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (T)field.GetValue(null)!; // justified: static enum field
            names.TryAdd(field.Name, value);
            var attribute = field.GetCustomAttribute<JsonEnumNameAttribute>();
            if (attribute != null)
            {
                names[attribute.Name] = value;
            }
        }

        return names;
    }

    private static Dictionary<T, string> BuildReverse()
    {
        var names = new Dictionary<T, string>();
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (T)field.GetValue(null)!; // justified: static enum field
            var attribute = field.GetCustomAttribute<JsonEnumNameAttribute>();
            names.TryAdd(value, attribute?.Name ?? field.Name);
        }

        return names;
    }

    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
        {
            return (T)Enum.ToObject(typeof(T), number);
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Unexpected token {reader.TokenType} for enum {typeof(T).Name}.");
        }

        var value = reader.GetString();
        if (value != null && ByName.TryGetValue(value, out var result))
        {
            return result;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return (T)Enum.ToObject(typeof(T), numeric);
        }

        throw new JsonException($"Unknown enum value: {value}");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (ToName.TryGetValue(value, out var name))
        {
            writer.WriteStringValue(name);
        }
        else
        {
            writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
    }
}
