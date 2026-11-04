using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using TabTabGo.Core.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;


namespace TabTabGo.Core;

/// <summary>
/// Serialize engine to handle all serialization and deserialization
/// </summary>
public static class SerializerEngine
{
    private static readonly JsonSerializerOptions SharedOptions = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
        options.AddTabTabGoConverters();
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>
    /// Default Json Serialization Settings. The instance is shared and read-only (mutating it throws);
    /// use <see cref="CreateJsonSerializerOptions"/> to get a mutable copy.
    /// </summary>
    public static JsonSerializerOptions JsonSerializationSettings => SharedOptions;

    /// <summary>
    /// Creates a mutable copy of <see cref="JsonSerializationSettings"/>.
    /// </summary>
    public static JsonSerializerOptions CreateJsonSerializerOptions() => new(SharedOptions);

    #region Xml Serialization

    /// <summary>
    /// Serialize Object to xml 
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static string ObjectToXml(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var xsSerializer = new XmlSerializer(obj.GetType());
        var xml = string.Empty;

        using (var sww = new StringWriter())
        {
            using (XmlWriter writer = XmlWriter.Create(sww))
            {
                xsSerializer.Serialize(writer, obj);
                xml = sww.ToString(); // Your XML
            }
        }

        return xml;
    }

    /// <summary>
    /// Deserialize xml to object
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="xml"></param>
    /// <returns></returns>
    public static T? XmlToObject<T>(string xml) where T : class
    {
        ArgumentNullException.ThrowIfNull(xml);
        var xsSerializer = new XmlSerializer(typeof(T));
        using TextReader reader = new StringReader(xml);
        return xsSerializer.Deserialize(reader) as T;
    }

    /// <summary>
    /// Convert xml to json
    /// </summary>
    /// <param name="xml"></param>
    /// <returns></returns>
    public static string XmlToJson(string xml)
    {
        var doc = XDocument.Parse(xml);
        return XmlToJson(doc);
    }

    /// <summary>
    /// Serialize XmlNode to json
    /// </summary>
    /// <param name="xmlNode"></param>
    /// <returns></returns>
    public static string XmlToJson(XmlNode xmlNode)
    {
        ArgumentNullException.ThrowIfNull(xmlNode);
        return XmlToJson(XDocument.Parse(xmlNode.OuterXml));
    }

    /// <summary>
    /// Serialize XDocument to json
    /// </summary>
    /// <param name="xDocument"></param>
    /// <param name="empty"></param>
    /// <returns></returns>
    public static string XmlToJson(XDocument? xDocument, string empty = "{}")
    {
        if(xDocument == null) return empty;
        return xDocument.Root == null ? empty : XElementToJsonObject(xDocument.Root).ToString();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="xDocument"></param>
    /// <returns></returns>
    public static dynamic? XmlToDynamic(XDocument? xDocument)
    {
        var json = XmlToJson(xDocument);
        return JsonToDynamic(json);
    }

    /// <summary>
    /// Convert xml string to a dynamic json node
    /// </summary>
    /// <param name="xml">xml text; empty returns an empty object</param>
    public static dynamic? XmlToDynamic(string xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            dynamic obj = new JsonObject();
            return obj;
        }

        var json = XmlToJson(xml);
        return JsonToDynamic(json);
    }

    /// <summary>
    /// Children of the element become properties; attributes become "@name"; repeated child names become an array;
    /// an element with attributes and text keeps its text under "#text".
    /// </summary>
    private static JsonObject XElementToJsonObject(XElement? element)
    {
        var jsonObject = new JsonObject();
        if (element == null) return jsonObject;

        AddAttributes(element, jsonObject);
        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            JsonNode value = XElementToJsonValue(child);
            if (!jsonObject.TryGetPropertyValue(name, out var existing))
            {
                jsonObject[name] = value;
            }
            else if (existing is JsonArray array)
            {
                array.Add(value);
            }
            else
            {
                jsonObject[name] = new JsonArray(existing!.DeepClone(), value);
            }
        }

        return jsonObject;
    }

    private static JsonNode XElementToJsonValue(XElement element)
    {
        if (element.HasElements)
        {
            return XElementToJsonObject(element);
        }

        if (!element.HasAttributes)
        {
            return JsonValue.Create(element.Value)!;
        }

        var jsonObject = new JsonObject();
        AddAttributes(element, jsonObject);
        if (element.Value.Length > 0)
        {
            jsonObject["#text"] = element.Value;
        }

        return jsonObject;
    }

    private static void AddAttributes(XElement element, JsonObject target)
    {
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            target["@" + attribute.Name.LocalName] = attribute.Value;
        }
    }

    #endregion

    #region Dynamic Serialization

    /// <summary>Serialize a dynamic value to json text.</summary>
    public static string DynamicToJson(dynamic? dObject)
    {
        return JsonSerializer.Serialize(dObject, JsonSerializationSettings);
    }

    /// <summary>Serialize a dynamic value to xml.</summary>
    public static XDocument DynamicToXml(dynamic? dObject)
    {
        var json = DynamicToJson(dObject);
        return JsonToXml(json);
    }

    #endregion

    #region Json Serialization

    /// <summary>
    /// Serialize object to dynamic object
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static dynamic? ObjectToDynamic(object obj)
    {
        return JsonNode.Parse(ObjectToJson(obj));
    }

    /// <summary>
    /// Serialize object to json string 
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static string ObjectToJson(object obj)
    {
        return JsonSerializer.Serialize(obj, JsonSerializationSettings);
    }

    /// <summary>
    /// Deserialize json string to object
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="json"></param>
    /// <returns></returns>
    public static T? JsonToObject<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonSerializationSettings);
    }

    /// <summary>
    /// Convert json to xml
    /// </summary>
    /// <param name="json">Json string</param>
    /// <param name="rootFieldName"> root field name </param>
    /// <returns>converted XDocument from json</returns>
    public static XDocument JsonToXml(string json, string rootFieldName = "root")
    {
        if (string.IsNullOrEmpty(json))
        {
            return XDocument.Parse("<root/>");
        }

        var rootElement = JsonToXElement(json, rootFieldName);
        return new XDocument(rootElement);
    }

    private static XElement JsonToXElement(string json, string rootFieldName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonElementToXElement(new XElement(XmlConvert.EncodeLocalName(rootFieldName)), document.RootElement);
    }

    private static XElement JsonElementToXElement(XElement parent, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    if (prop.Name.StartsWith('@') && prop.Name.Length > 1 && prop.Value.ValueKind != JsonValueKind.Object
                        && prop.Value.ValueKind != JsonValueKind.Array)
                    {
                        parent.SetAttributeValue(XmlConvert.EncodeLocalName(prop.Name[1..]), prop.Value.ToString());
                    }
                    else if (prop.Name == "#text")
                    {
                        parent.Add(prop.Value.ToString());
                    }
                    else
                    {
                        parent.Add(JsonElementToXElement(new XElement(XmlConvert.EncodeLocalName(prop.Name)), prop.Value));
                    }
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (JsonElement value in element.EnumerateArray())
                {
                    parent.Add(JsonElementToXElement(new XElement($"{parent.Name}{i++}"), value));
                }
                break;
            default:
                parent.Value = element.ToString();
                break;
        }
        return parent;
    }
    /// <summary>
    /// Parse json string to dynamic object
    /// </summary>
    /// <param name="json"></param>
    /// <returns></returns>
    public static dynamic? JsonToDynamic(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "null")
        {
            dynamic obj = new JsonObject();
            return obj;
        }

        return JsonNode.Parse(json);
    }

    #endregion

    #region Enum Serialization

    /// <summary>
    /// Convert an enum type to a json array of <c>{ id, name }</c>
    /// </summary>
    /// <param name="enumerationType">enum type</param>
    public static dynamic EnumToDynamic(Type enumerationType)
    {
        ArgumentNullException.ThrowIfNull(enumerationType);
        var json = new JsonArray();
        foreach (var enumeration in Enum.GetValues(enumerationType))
        {
            json.Add(EnumToJsonObject(enumeration));
        }

        return json;
    }

    /// <summary>
    /// Convert an enum type to a json string of <c>{ id, name }</c> items
    /// </summary>
    /// <param name="enumerationType">enum type</param>
    public static string EnumToJson(Type enumerationType)
    {
        return DynamicToJson(EnumToDynamic(enumerationType));
    }

    /// <summary>
    /// Convert one enum value to a json object <c>{ id, name }</c>
    /// </summary>
    /// <param name="enumeration">enum value</param>
    public static dynamic EnumToDynamic(object enumeration)
    {
        ArgumentNullException.ThrowIfNull(enumeration);
        return EnumToJsonObject(enumeration);
    }

    /// <summary>
    /// Convert one enum value to a json string <c>{ id, name }</c>
    /// </summary>
    /// <param name="enumeration">enum value</param>
    public static string EnumToJson(object enumeration)
    {
        return DynamicToJson(EnumToDynamic(enumeration));
    }

    private static JsonObject EnumToJsonObject(object enumeration) => new()
    {
        ["id"] = Convert.ToInt64(enumeration, CultureInfo.InvariantCulture),
        ["name"] = enumeration.ToString() ?? string.Empty,
    };

    #endregion

    #region Map Dictionary To Json

    /// <summary>
    /// converts key-value pair  into a dynamic object (json)  
    /// </summary>
    /// <example>
    /// a dictionary with following items:-
    ///     prop1               val1
    ///     obj1_subprop1       subval1
    ///     obj1_subprop2       subval2
    ///     arr1_0_item         arrayval1
    ///     arr1_1_item         arrayval2
    ///     prop2               val2
    ///will convert into following JsonObject
    ///{
    ///     prop1: "val1",
    ///     obj1: {
    ///         subprop1: "subval1",
    ///         subprop2: "subval2"
    ///     },
    ///     arr1: [
    ///         {item: "arrayval1"},
    ///         {item: "arrayval2"}
    ///     ],
    ///     prop2: "val2"
    /// }
    /// </example>
    /// <param name="dictionary"></param>
    /// <param name="separator">list of separator for property path. default is "_" and "." </param>
    /// <returns>JsonObject</returns>
    public static JsonObject ConvertDictionaryToJson(Dictionary<string, string?> dictionary, char[]? separator = null)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        var jResult = new JsonObject();
        separator ??= new[] { '_', '.' };
        foreach (var keyValuePair in dictionary)
        {
            MapToJsonObject(keyValuePair.Key.Split(separator), jResult, keyValuePair.Value);
        }

        return jResult;
    }

    /// <summary>
    /// Map keyvalue pair to json object 
    /// </summary>
    /// <param name="tokens">property path in list of token</param>
    /// <param name="jObj">Json Object</param>
    /// <param name="value">Property path value</param>
    public static void MapToJsonObject(string[] tokens, JsonObject jObj, string? value)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(jObj);
        var current = jObj;
        for (var t = 0; t < tokens.Length; t++)
        {
            var name = tokens[t];
            var isLast = t == tokens.Length - 1;
            if (isLast)
            {
                current[name] = ParseScalar(value);
                return;
            }

            if (TryGetIndex(tokens[t + 1], out var index))
            {
                if (current[name] is not JsonArray array)
                {
                    array = new JsonArray();
                    current[name] = array;
                }

                while (array.Count <= index)
                {
                    array.Add(null);
                }

                if (t + 1 == tokens.Length - 1)
                {
                    // primitive array item: arr_0 = value
                    array[index] = ParseScalar(value);
                    return;
                }

                if (array[index] is not JsonObject element)
                {
                    element = new JsonObject();
                    array[index] = element;
                }

                current = element;
                t++; // the index token is consumed
            }
            else
            {
                if (current[name] is not JsonObject child)
                {
                    child = new JsonObject();
                    current[name] = child;
                }

                current = child;
            }
        }
    }

    private static bool TryGetIndex(string token, out int index) =>
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out index);

    private static JsonNode ParseScalar(string? value)
    {
        var text = value ?? string.Empty;
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var longVal))
            return JsonValue.Create(longVal);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal) && double.IsFinite(doubleVal))
            return JsonValue.Create(doubleVal);
        if (bool.TryParse(text, out var booleanVal))
            return JsonValue.Create(booleanVal);
        return JsonValue.Create(text);
    }

    #endregion

    #region Map Json To Dictionary

    /// <summary>
    /// converts a dynamic object (json) into a keyvalue pair where both key and value are strings
    /// </summary>
    /// <example>
    /// Following JsonObject :-
    /// {
    ///     prop1: "val1",
    ///     obj1: {
    ///         subprop1: "subval1",
    ///         subprop2: "subval2"
    ///     },
    ///     arr1: [
    ///         {item: "arrayval1"},
    ///         {item: "arrayval2"}
    ///     ],
    ///     prop2: "val2"
    /// }
    ///Will be converted to a dictionary with following items :-
    ///     prop1               val1
    ///     obj1_subprop1       subval1
    ///     obj1_subprop2       subval2
    ///     arr1_0_item         arrayval1
    ///     arr1_0_item         arrayval2
    ///     prop2               val2
    /// </example>
    /// <param name="jObj">dynamic object (json)</param>
    /// <param name="separator">properties path separator. default is "_"</param>
    /// <returns><![CDATA[Dictionary<string,string>]]></returns> 
    public static Dictionary<string, string> ConvertJsonToDictionary(JsonObject? jObj, string? separator = null)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(separator))
        {
            separator = "_";
        }

        if (jObj == null) return result;
        Flatten(string.Empty, jObj, separator, result);
        return result;
    }

    private static void Flatten(string path, JsonNode? node, string separator, Dictionary<string, string> result)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var (name, child) in jsonObject)
                {
                    Flatten(path.Length == 0 ? name : path + separator + name, child, separator, result);
                }

                break;
            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    Flatten(path + separator + index.ToString(CultureInfo.InvariantCulture), jsonArray[index], separator, result);
                }

                break;
            default:
                result[path] = node?.ToString() ?? string.Empty;
                break;
        }
    }

    #endregion
}