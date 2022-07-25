using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace TabTabGo.Core;

/// <summary>
/// TODO make sure create extenstion for each function
/// </summary>
public static class SerializerEngine
{
    private static JsonSerializerSettings _jsonSerializationSettings;
    private static JsonSerializerSettings GetJsonSerializationSettings()
    {
        if (_jsonSerializationSettings == null)
        {
            _jsonSerializationSettings = new JsonSerializerSettings();
            _jsonSerializationSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
            _jsonSerializationSettings.PreserveReferencesHandling = Newtonsoft.Json.PreserveReferencesHandling.None;
        }

        return _jsonSerializationSettings;
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
    public static T XmlToObject<T>(string xml) where T : class
    {
        var xsSerializer = new XmlSerializer(typeof(T));
        T result = null;
        using (TextReader reader = new StringReader(xml))
        {
            result = (T)xsSerializer.Deserialize(reader);
        }

        return result;
    }

    public static string XmlToJson(string xml)
    {
        var doc = XDocument.Parse(xml);
        return XmlToJson(doc);
    }

    public static string XmlToJson(XDocument xDocument, string empty = "{}")
    {
        var sJson = JsonConvert.SerializeXNode(xDocument, Newtonsoft.Json.Formatting.None, true);
        return sJson == "null" ? empty : sJson;
    }

    public static string XmlToJson(XmlDocument xDocument, string empty = "{}")
    {
        var sJson = JsonConvert.SerializeXmlNode(xDocument, Newtonsoft.Json.Formatting.None, true);
        return sJson == "null" ? empty : sJson;
    }

    public static dynamic XmlToDynamic(XDocument xDocument)
    {
        var json = XmlToJson(xDocument);
        return JsonToDynamic(json);
    }

    public static dynamic XmlToDynamic(string xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            dynamic obj = new JObject();
            return obj;
        }
        var json = XmlToJson(xml);
        return JsonToDynamic(json);
    }
    #endregion

    #region Dynamic Serialization
    public static string DynamicToJson(dynamic dObject)
    {
        return JsonConvert.SerializeObject(dObject, GetJsonSerializationSettings());
    }

    public static XDocument DynamicToXml(dynamic dObject)
    {
        var json = DynamicToJson(dObject);
        return JsonToXml(json);
    }
    #endregion

    #region Json Serialization

    public static dynamic ObjectToDynamic(object obj)
    {
        return JObject.Parse(ObjectToJson(obj));
    }

    public static string ObjectToJson(object obj)
    {
        return JsonConvert.SerializeObject(obj, GetJsonSerializationSettings());
    }

    public static T JsonToObject<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json, GetJsonSerializationSettings());
    }

    public static XDocument JsonToXml(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            var xDoc = XDocument.Parse("<root/>");

            return xDoc;
        }
        return JsonConvert.DeserializeXNode(json, "root", true);
    }

    public static dynamic JsonToDynamic(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "null")
        {
            dynamic obj = new JObject();
            return obj;
        }
        return JObject.Parse(json);
    }
    #endregion

    #region Enum Serialization
    public static dynamic EnumToDynamic(Type enumerationType)
    {
        dynamic json = new JArray() as dynamic;

        foreach (var enumeration in Enum.GetValues(enumerationType))
        {
            var obj = new JObject() as dynamic;
            obj.id = (int)enumeration;
            obj.name = enumeration.ToString();

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
        var obj = new JObject() as dynamic;
        obj.id = (int)enumeration;
        obj.name = enumeration.ToString();

        return obj;
    }

    public static string EnumToJson(object enumeration)
    {
        return DynamicToJson(EnumToDynamic(enumeration));
    }
    #endregion

    #region Map Dictionary To Json
    /// <summary>
    /// converts keyvalue pair  into a dynamic object (json)  
    /// </summary>
    /// <example>
    /// a dictionary with following items:-
    ///     prop1               val1
    ///     obj1_subprop1       subval1
    ///     obj1_subprop2       subval2
    ///     arr1_0_item         arrayval1
    ///     arr1_1_item         arrayval2
    ///     prop2               val2
    ///will convert into following JObject
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
    /// <returns>JObject</returns>
    public static JObject ConvertDictionaryToJson(Dictionary<string, string> dictionary, char[] separator = null)
    {
        var jResult = new JObject();
        if (separator == null)
        {
            separator = new[] { '_', '.' };
        }
        foreach (var keyValuePair in dictionary)
        {
            MapToJObject(keyValuePair.Key.Split(separator), jResult, keyValuePair.Value);
        }

        return jResult;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="tokens">property path in list of token</param>
    /// <param name="jObj">Json Object</param>
    /// <param name="value">Property path value</param>
    public static void MapToJObject(string[] tokens, JObject jObj, string value)
    {
        var jCurrent = jObj;
        for (int t = 0; t < tokens.Length; t++)
        {
            if (t < tokens.Length - 1)
            {
                bool isArray = false;
                int index = 0;
                isArray = int.TryParse(tokens[t + 1], out index);
                if (jCurrent == null)
                {
                    jCurrent = new JObject(tokens[t]);
                }
                else if (jCurrent[tokens[t]] == null) // || !(jCurrent[tokens[t]] is JObject) || !(jCurrent[tokens[t]] is JArray))
                {
                    if (isArray)
                    {
                        jCurrent.Add(new JProperty(tokens[t], new JArray()));
                        JArray currentArray = jCurrent[tokens[t]] as JArray;
                        currentArray.Insert(index, new JObject());
                        jCurrent = currentArray[index] as JObject;
                        t++;
                    }
                    else
                    {
                        jCurrent.Add(new JProperty(tokens[t], new JObject()));
                        jCurrent = jCurrent[tokens[t]] as JObject;
                    }
                }
                else
                {
                    if (isArray)
                    {
                        JArray currentArray = jCurrent[tokens[t]] as JArray;
                        if (currentArray.Count <= index)
                        {
                            currentArray.Insert(index, new JObject());
                            jCurrent = currentArray[index] as JObject;
                        }
                        else
                        {
                            jCurrent = currentArray[index] as JObject;
                        }
                        t++;
                    }
                    else
                    {
                        jCurrent = jCurrent[tokens[t]] as JObject;
                    }
                }
            }
            else
            {
                JProperty prop = null;

                if (jCurrent[tokens[t]] == null)
                {
                    prop = new JProperty(tokens[t], null);
                    jCurrent.Add(prop);
                }
                else
                {
                    prop = jCurrent[tokens[t]] as JProperty;
                }

                string stringVal = value ?? string.Empty;
                long longVal = 0;
                double doubleVal = 0.0;
                bool booleanVal = false;
                bool isLong = long.TryParse(stringVal, out longVal);
                bool isDouble = double.TryParse(stringVal, out doubleVal);
                bool isBoolean = bool.TryParse(stringVal, out booleanVal);
                if (isLong)
                    prop.Value = longVal;
                else if (isDouble)
                    prop.Value = doubleVal;
                else if (isBoolean)
                    prop.Value = booleanVal;
                else
                    prop.Value = stringVal;
            }

        }
    }
    #endregion

    #region Map Json To Dictionary
    /// <summary>
    /// converts a dynamic object (json) into a keyvalue pair where both key and value are strings
    /// </summary>
    /// <example>
    /// Following JObject :-
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
    /// <param name="jobj">dynamic object (json)</param>
    /// <param name="separator">properties path separator. default is "_"</param>
    /// <returns><![CDATA[Dictionary<string,string>]]></returns> 
    public static Dictionary<string, string> ConvertJsonToDictionary(JObject jobj, string separator = null)
    {
        if (string.IsNullOrEmpty(separator))
        {
            separator = "_";
        }

        Dictionary<string, string> result = new Dictionary<string, string>();

        foreach (KeyValuePair<string, JToken> kvJson in jobj)
        {
            AddKeyValue("", kvJson.Key, kvJson.Value, separator, result);
        }

        return result;
    }

    private static void AddKeyValue(string fullkey, string key, JToken token, string separator, Dictionary<string, string> result)
    {
        Type tokenType = token.GetType();

        if (tokenType == typeof(JValue))
        {
            string newKey = fullkey;
            if (!string.IsNullOrEmpty(newKey))
                newKey += separator + key;
            else
                newKey = key;

            result.Add(newKey, token.ToString());
        }
        else
        {
            if (string.IsNullOrEmpty(fullkey))
                fullkey = key;
            else
                fullkey = fullkey + separator + key;

            if (tokenType == typeof(JObject))
            {
                foreach (KeyValuePair<string, JToken> kvJson in ((JObject)token))
                {
                    AddKeyValue(fullkey, kvJson.Key, kvJson.Value, separator, result);
                }
            }
            if (tokenType == typeof(JArray))
            {
                JArray arrToken = (JArray)token;
                for (int index = 0; index < arrToken.Count; index++)
                {
                    foreach (KeyValuePair<string, JToken> kvJson in ((JObject)arrToken[index]))
                    {
                        string arrayKey = string.Empty;
                        if (string.IsNullOrEmpty(fullkey))
                            arrayKey = key + separator + index.ToString();
                        else
                            arrayKey = fullkey + separator + index.ToString();

                        AddKeyValue(arrayKey, kvJson.Key, kvJson.Value, separator, result);
                    }
                }
            }
        }
    }
    #endregion

}

