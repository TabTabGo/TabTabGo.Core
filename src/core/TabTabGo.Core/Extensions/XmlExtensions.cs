// ReSharper disable All
using System.Xml;
using System.Xml.Linq;

namespace TabTabGo.Core.Extensions;

public static class XmlExtensions
{
    /// <summary>
    /// Convert xml string to Json Object
    /// </summary>
    /// <param name="xml"></param>
    /// <returns></returns>
    public static JsonObject? ConvertToJson(this string xml)
    {
        if (string.IsNullOrEmpty(xml)) new JsonObject();
        var sJson = SerializerEngine.XmlToJson(xml);
        return JsonNode.Parse(sJson) as JsonObject;
    }

    /// <summary>
    /// Convert xml string to Json Array
    /// </summary>
    /// <param name="xml"></param>
    /// <returns></returns>
    public static JsonArray ConvertToJsonArray(this string xml)
    {
        return ConvertToJsonArray(xml, "root");
    }

    /// <summary>
    /// Convert xml string to Json Array
    /// </summary>
    /// <param name="xml"></param>
    /// <param name="rootFieldName"></param>
    /// <returns></returns>
    public static JsonArray ConvertToJsonArray(this string xml, string rootFieldName)
    {
        var jsonObject = ConvertToJson(xml);
        if (jsonObject != null && jsonObject.ContainsKey(rootFieldName) && jsonObject[rootFieldName] is JsonArray)
        {
            return jsonObject[rootFieldName] as JsonArray ?? new JsonArray();
        }
        return new JsonArray();
    }

    public static JsonObject ConvertToJson(this XmlDocument? xmlDoc)
    {
        return xmlDoc == null ? new JsonObject() : JsonNode.Parse(SerializerEngine.XmlToJson(xmlDoc)) as JsonObject ?? new JsonObject();

    }

    public static JsonObject? ConvertToJson(this XDocument? xmlDoc)
    {
        return xmlDoc == null ? new JsonObject() : JsonNode.Parse(SerializerEngine.XmlToJson(xmlDoc)) as JsonObject;
    }

    public static IDictionary<string, object> ConvertToDictionary(this XDocument xmlDoc,
        bool usePathAsKey = false,
        string? separator = null)
    {
        //TODO direct convert to dictionary
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }

    public static IDictionary<string, object> ConvertToDictionary(this XmlDocument xmlDoc,
        bool usePathAsKey = false,
        string? separator = null)
    {
        //TODO direct convert to dictionary
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }

    public static IDictionary<string, object> ConvertToDictionary(this string xmlDoc, 
        bool usePathAsKey = false,
        string? separator = null)
    {
        //TODO direct convert to dictionary
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }
}

