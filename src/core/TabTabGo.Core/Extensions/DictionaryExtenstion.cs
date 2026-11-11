namespace TabTabGo.Core.Extensions;

/// <summary>
/// Helpers for dictionaries.
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>
    /// Compares two dictionaries by keys and values.
    /// </summary>
    /// <see href="https://stackoverflow.com/questions/3928822/comparing-2-dictionarystring-string-instances"/>
    public static bool DictionaryEqual<TKey, TValue>(
        this IDictionary<TKey, TValue>? first, IDictionary<TKey, TValue>? second)
    {
        return first.DictionaryEqual(second, null);
    }

    /// <summary>
    /// Compares two dictionaries by keys and values using a value comparer.
    /// </summary>
    public static bool DictionaryEqual<TKey, TValue>(
        this IDictionary<TKey, TValue>? first, IDictionary<TKey, TValue>? second,
        IEqualityComparer<TValue>? valueComparer)
    {
        if (first == second) return true;
        if ((first == null) || (second == null)) return false;
        if (first.Count != second.Count) return false;

        valueComparer ??= EqualityComparer<TValue>.Default;

        foreach (var (key, value) in first)
        {
            if (!second.TryGetValue(key, out var secondValue)) return false;
            if (!valueComparer.Equals(value, secondValue)) return false;
        }
        return true;
    }

    /// <summary>
    /// Sets the value of an entry, adding it when missing.
    /// </summary>
    public static void AddOrUpdate<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        dictionary[key] = value;
    }

    /// <summary>
    /// Convert Dictionary to XML
    /// </summary>
    /// <param name="dictionary">dictionary object</param>
    /// <param name="usePathAsKey">treat keys as property paths</param>
    /// <param name="separator">path separators</param>
    public static string ConvertToXml(this IDictionary<string, object?>? dictionary, bool usePathAsKey = false, char[]? separator = null)
    {
        if (dictionary == null) return string.Empty;
        var jsonObject = ConvertToJson(dictionary, usePathAsKey, separator);

        return jsonObject?.ConvertToXml() ?? "{}";
    }

    /// <summary>
    /// Convert Dictionary to Json
    /// </summary>
    /// <param name="dictionary">dictionary object</param>
    /// <param name="usePathAsKey">treat keys as property paths</param>
    /// <param name="separator">path separators</param>
    public static JsonNode? ConvertToJson(this IDictionary<string, object?>? dictionary, bool usePathAsKey = false, char[]? separator = null)
    {
        if (dictionary == null) return new JsonObject();
        return usePathAsKey
            ? SerializerEngine.ConvertDictionaryToJson(dictionary.ToDictionary(d => d.Key, d => d.Value?.ToString()), separator)
            : JsonNode.Parse(JsonSerializer.Serialize(dictionary, SerializerEngine.JsonSerializationSettings));
    }
}
