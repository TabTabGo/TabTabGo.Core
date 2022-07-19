using System.Xml;
using System.Xml.Linq;

namespace TabTabGo.Core.Extensions;

public static class XmlExtensions
{
    public static JObject ConvertToJson(this string xml)
    {
        if (string.IsNullOrEmpty(xml)) new JObject();
        var sJson = SerializerEngine.XmlToJson(xml);
        return JObject.Parse(sJson);
    }

    public static JArray ConvertToJArray(this string xml)
    {
        return ConvertToJArray(xml, "root");
    }

    public static JArray ConvertToJArray(this string xml, string rootFieldName)
    {
        var jObject = ConvertToJson(xml);
        if (jObject.ContainsKey(rootFieldName) && jObject[rootFieldName] is JArray)
        {
            return jObject[rootFieldName] as JArray;
        }
        return new JArray();
    }

    public static JObject ConvertToJson(this XmlDocument xmlDoc)
    {
        return xmlDoc == null ? new JObject() : JObject.Parse(SerializerEngine.XmlToJson(xmlDoc));

    }

    public static JObject ConvertToJson(this XDocument xmlDoc)
    {
        return xmlDoc == null ? new JObject() : JObject.Parse(SerializerEngine.XmlToJson(xmlDoc));
    }

    public static IDictionary<string, object> ConvertToDictionary(this XDocument xmlDoc, bool usePathAsKey = false, string separator = null)
    {
        //TODO direct convert to dictionaru
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }

    public static IDictionary<string, object> ConvertToDictionary(this XmlDocument xmlDoc, bool usePathAsKey = false, string separator = null)
    {
        //TODO direct convert to dictionaru
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }

    public static IDictionary<string, object> ConvertToDictionary(this string xmlDoc, bool usePathAsKey = false, string separator = null)
    {
        //TODO direct convert to dictionaru
        var jsonObject = ConvertToJson(xmlDoc);
        return jsonObject.ConvertToDictionary(usePathAsKey, separator);
    }
}

