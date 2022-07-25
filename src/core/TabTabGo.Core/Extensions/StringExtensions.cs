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
    
    public static JObject DeserializeToJson(this string json)
    {
        return JObject.Parse(json);
    }

    public static JArray DeserializeToJArray(this string json)
    {
        return JArray.Parse(json);
    }

    public static T? Deserialize<T>(this string serializedObj)
    {
        return JsonConvert.DeserializeObject<T>(serializedObj, JsonExtensions.JsonOptions);
    }
}

