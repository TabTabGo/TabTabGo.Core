namespace TabTabGo.Core.Extensions;

public static class StringExtensions
{
    public static string FirstCharToUpper(this string input)
    {
        switch (input)
        {
            case null: throw new ArgumentNullException(nameof(input));
            case "": throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input));
            default: return input.First().ToString().ToUpper() + input.Substring(1);
        }
    }
    
    public static JsonObject? DeserializeToJson(this string json)
    {
        return JsonNode.Parse(json) as JsonObject;
    }

    public static JsonArray? DeserializeToJArray(this string json)
    {
        return JsonNode.Parse(json) as JsonArray;
    }

    public static T? Deserialize<T>(this string serializedObj)
    {
        return JsonSerializer.Deserialize<T>(serializedObj, JsonExtensions.JsonOptions);
    }
}

