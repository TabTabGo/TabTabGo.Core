
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;


namespace TabTabGo.Core;

/// <summary>
/// Serialize engine to handle all serialization and deserialization
/// </summary>
public static class SerializerEngine
{
    private static JsonSerializerOptions? _jsonSerializationSettings = null;

    /// <summary>
    /// Default Json Serialization Settings
    /// </summary>
    /// <returns></returns>
    public static JsonSerializerOptions? JsonSerializationSettings
    {
        get
        {
            _jsonSerializationSettings ??= new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                // PropertyNameCaseInsensitive = true, // support sensitive case
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

            return _jsonSerializationSettings;
        }
    }


    #region Xml Serialization

    /// <summary>
    /// Serialize Object to xml 
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static string ObjectToXml(object obj)
    {
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
        var xsSerializer = new XmlSerializer(typeof(T));
        T? result = null;
        using TextReader reader = new StringReader(xml);
        result = (T)xsSerializer.Deserialize(reader)!;

        return result;
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
        // Load the XML into an XmlDocument
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xmlNode.OuterXml);

        // Convert the XmlDocument to a JsonDocument
        var jsonDocument = JsonDocument.Parse(XmlToJson(doc));

        // Serialize the JsonDocument to a JSON string
        return JsonSerializer.Serialize(jsonDocument);
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
        var sJson = XElementToJsonObject(xDocument.Root);
        return sJson != null ? sJson.ToString() : empty ;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="xDocument"></param>
    /// <returns></returns>
    public static dynamic? XmlToDynamic(XDocument xDocument)
    {
        var json = XmlToJson(xDocument);
        return JsonToDynamic(json);
    }

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

    private static JsonObject XElementToJsonObject(XElement? element)
    {
        if(element == null) return new JsonObject();
        JsonObject jsonObject = new JsonObject();

        foreach (XElement child in element.Elements())
        {
            if (child.HasElements)
            {
                jsonObject.Add(child.Name.LocalName, XElementToJsonObject(child));
            }
            else
            {
                jsonObject.Add(child.Name.LocalName, child.Value);
            }
        }

        return jsonObject;
    }

    #endregion

    #region Dynamic Serialization

    public static string DynamicToJson(dynamic dObject)
    {
        return JsonSerializer.Serialize(dObject, JsonSerializationSettings);
    }

    public static XDocument DynamicToXml(dynamic dObject)
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
    public static XDocument? JsonToXml(string json, string rootFieldName = "root")
    {
        if (string.IsNullOrEmpty(json))
        {
            var xDoc = XDocument.Parse("<root/>");
            return xDoc;
        }

        var rootElement = JsonToXElement(json, rootFieldName);
        return new XDocument(rootElement);
    }

    private static XElement JsonToXElement(string json, string rootFieldName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonElementToXElement(new XElement(rootFieldName), document.RootElement);
    }

    private static XElement JsonElementToXElement(XElement parent, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    parent.Add(JsonElementToXElement(new XElement(prop.Name), prop.Value));
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

    public static dynamic EnumToDynamic(Type enumerationType)
    {
        dynamic json = new JsonArray() as dynamic;

        foreach (var enumeration in Enum.GetValues(enumerationType))
        {
            var obj = new JsonObject() as dynamic;
            obj.id = (int)enumeration;
            obj.name = enumeration.ToString() ?? string.Empty;

            json.Add(obj);
        }

        return json;
    }

    public static string EnumToJson(Type enumerationType)
    {
        return DynamicToJson(EnumToDynamic(enumerationType));
    }

    public static dynamic EnumToDynamic(object enumeration)
    {
        var obj = new JsonObject() as dynamic;
        obj.id = (int)enumeration;
        obj.name = enumeration?.ToString() ?? string.Empty;

        return obj;
    }

    public static string EnumToJson(object enumeration)
    {
        return DynamicToJson(EnumToDynamic(enumeration));
    }

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
    public static void MapToJsonObject(string[] tokens, JsonObject jObj, string value)
    {
        var jCurrent = jObj;
        for (int t = 0; t < tokens.Length; t++)
        {
            if (t < tokens.Length - 1)
            {
                var isArray = int.TryParse(tokens[t + 1], out var index);
                if (jCurrent == null)
                {
                    jCurrent = new JsonObject() { { tokens[t], isArray ? new JsonArray() : new JsonObject() } };
                }
                else if
                    (jCurrent[tokens[t]] ==
                     null) // || !(jCurrent[tokens[t]] is JsonObject) || !(jCurrent[tokens[t]] is JsonArray))
                {
                    if (isArray)
                    {
                        var currentArray = new JsonArray();
                        currentArray.Insert(index, new JsonObject());
                        jCurrent.Add(tokens[t], currentArray);
                        jCurrent = currentArray[index] as JsonObject;
                        t++;
                    }
                    else
                    {
                        jCurrent.Add(tokens[t], new JsonObject());
                        jCurrent = jCurrent[tokens[t]] as JsonObject;
                    }
                }
                else
                {
                    if (isArray)
                    {
                        var currentArray = jCurrent[tokens[t]] as JsonArray ?? new JsonArray();
                        if (currentArray.Count <= index)
                        {
                            currentArray.Insert(index, new JsonObject());
                            jCurrent = currentArray[index] as JsonObject;
                        }
                        else
                        {
                            jCurrent = currentArray[index] as JsonObject;
                        }

                        t++;
                    }
                    else
                    {
                        jCurrent = jCurrent[tokens[t]] as JsonObject;
                    }
                }
            }
            else
            {
                jCurrent ??= new JsonObject();
                string stringVal = value ?? string.Empty;
                bool isLong = long.TryParse(stringVal, out var longVal);
                bool isDouble = double.TryParse(stringVal, out var doubleVal);
                bool isBoolean = bool.TryParse(stringVal, out var booleanVal);
                if (isLong)
                    jCurrent.Add(tokens[t], longVal);
                else if (isDouble)
                    jCurrent.Add(tokens[t], doubleVal);
                else if (isBoolean)
                    jCurrent.Add(tokens[t], booleanVal);
                else
                    jCurrent.Add(tokens[t], stringVal);
            }
        }
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
        foreach (KeyValuePair<string, JsonNode> kvJson in jObj)
        {
            AddKeyValue("", kvJson.Key, kvJson.Value, separator, result);
        }

        return result;
    }

    private static void AddKeyValue(string fullKey, string key, JsonNode token, string separator,
        Dictionary<string, string> result)
    {
        Type tokenType = token.GetType();

        if (tokenType == typeof(JsonValue))
        {
            string newKey = fullKey;
            if (!string.IsNullOrEmpty(newKey))
                newKey += separator + key;
            else
                newKey = key;

            result.Add(newKey, token.ToString());
        }
        else
        {
            if (string.IsNullOrEmpty(fullKey))
                fullKey = key;
            else
                fullKey = fullKey + separator + key;

            if (tokenType == typeof(JsonObject))
            {
                foreach (KeyValuePair<string, JsonNode> kvJson in ((JsonObject)token))
                {
                    AddKeyValue(fullKey, kvJson.Key, kvJson.Value, separator, result);
                }
            }

            if (tokenType == typeof(JsonArray))
            {
                JsonArray arrToken = (JsonArray)token;
                for (int index = 0; index < arrToken.Count; index++)
                {
                    foreach (KeyValuePair<string, JsonNode> kvJson in ((JsonObject)arrToken[index]))
                    {
                        string arrayKey = string.Empty;
                        if (string.IsNullOrEmpty(fullKey))
                            arrayKey = key + separator + index.ToString();
                        else
                            arrayKey = fullKey + separator + index.ToString();

                        AddKeyValue(arrayKey, kvJson.Key, kvJson.Value, separator, result);
                    }
                }
            }
        }
    }

    #endregion
}