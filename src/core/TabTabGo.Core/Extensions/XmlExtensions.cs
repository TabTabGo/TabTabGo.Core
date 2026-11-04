using System.Xml;
using System.Xml.Linq;

namespace TabTabGo.Core.Extensions;

/// <summary>
/// Helpers that convert XML to JSON nodes and dictionaries.
/// </summary>
public static class XmlExtensions
{
    /// <summary>
    /// Convert xml string to Json Object
    /// </summary>
    public static JsonObject? ConvertToJson(this string xml)
    {
        if (string.IsNullOrEmpty(xml)) return new JsonObject();
        var sJson = SerializerEngine.XmlToJson(xml);
        return JsonNode.Parse(sJson) as JsonObject;
    }

    /// <summary>
    /// Convert xml string to Json Array
    /// </summary>
    public static JsonArray ConvertToJsonArray(this string xml)
    {
        return ConvertToJsonArray(xml, "root");
    }

    /// <summary>
    /// Convert xml string to Json Array
    /// </summary>
    /// <param name="xml">xml text</param>
    /// <param name="rootFieldName">name of the property that holds the array</param>
    public static JsonArray ConvertToJsonArray(this string xml, string rootFieldName)
    {
        var jsonObject = ConvertToJson(xml);
        if (jsonObject != null && jsonObject.ContainsKey(rootFieldName) && jsonObject[rootFieldName] is JsonArray array)
        {
            return JsonNode.Parse(array.ToJsonString())!.AsArray(); // justified: parsing text we just produced as valid JSON
        }
        return new JsonArray();
    }

    /// <summary>Convert an XmlDocument to a JSON object.</summary>
    public static JsonObject ConvertToJson(this XmlDocument? xmlDoc)
    {
        return xmlDoc == null ? new JsonObject() : JsonNode.Parse(SerializerEngine.XmlToJson(xmlDoc)) as JsonObject ?? new JsonObject();
    }

    /// <summary>Convert an XDocument to a JSON object.</summary>
    public static JsonObject? ConvertToJson(this XDocument? xmlDoc)
    {
        return xmlDoc == null ? new JsonObject() : JsonNode.Parse(SerializerEngine.XmlToJson(xmlDoc)) as JsonObject;
    }

    /// <summary>Convert an XDocument to a dictionary.</summary>
    public static IDictionary<string, object?> ConvertToDictionary(this XDocument xmlDoc,
        bool usePathAsKey = false,
        string? separator = null)
    {
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }

    /// <summary>Convert an XmlDocument to a dictionary.</summary>
    public static IDictionary<string, object?> ConvertToDictionary(this XmlDocument xmlDoc,
        bool usePathAsKey = false,
        string? separator = null)
    {
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }

    /// <summary>Convert xml text to a dictionary.</summary>
    public static IDictionary<string, object?> ConvertToDictionary(this string xmlDoc,
        bool usePathAsKey = false,
        string? separator = null)
    {
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }
}
