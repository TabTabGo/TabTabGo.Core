namespace TabTabGo.Core.Extensions;

/// <summary>
/// Helpers for strings.
/// </summary>
public static class StringExtensions
{
    /// <summary>Upper-cases the first character.</summary>
    public static string FirstCharToUpper(this string input)
    {
        switch (input)
        {
            case null: throw new ArgumentNullException(nameof(input));
            case "": throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input));
            default: return char.ToUpperInvariant(input[0]) + input.Substring(1);
        }
    }

    /// <summary>Parses JSON text to an object; null when the root is not an object.</summary>
    public static JsonObject? DeserializeToJson(this string json)
    {
        return JsonNode.Parse(json) as JsonObject;
    }

    /// <summary>Parses JSON text to an array; null when the root is not an array.</summary>
    public static JsonArray? DeserializeToJArray(this string json)
    {
        return JsonNode.Parse(json) as JsonArray;
    }

    /// <summary>Deserializes JSON text with the shared options.</summary>
    public static T? Deserialize<T>(this string serializedObj)
    {
        return JsonSerializer.Deserialize<T>(serializedObj, JsonExtensions.JsonOptions);
    }
}
