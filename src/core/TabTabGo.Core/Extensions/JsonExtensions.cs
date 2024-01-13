// ReSharper disable All

namespace TabTabGo.Core.Extensions
{
    public static class JsonExtensions
    {
        public static string? ConvertToXml(this string json)
        {
            return ConvertToXml(json, "root");
        }
        public static string?  ConvertToXml(this string json, string rootFieldName)
        {
            var xml = SerializerEngine.JsonToXml(json, rootFieldName);
            return xml?.ToString();
        }
        public static string ConvertToXml(this JsonNode jToken)
        {
            return ConvertToXml(jToken, "root");
        }
        public static string ConvertToXml(this JsonNode jToken , string rootFieldName)
        {
            string sToken;
            if(jToken is JsonArray )
            {
                var jObject = new JsonObject() { { rootFieldName, jToken } };
                sToken = jObject.ToString();
            }
            else
            {
                sToken = jToken?.ToString();
            }
            return ConvertToXml(sToken);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="jsonObject">Json Token Object</param>
        /// <param name="usePathAsKey">If true make json path into dictionary Key</param>
        /// <param name="separator">The </param>
        /// <returns></returns>
        public static IDictionary<string, object> ConvertToDictionary(this JsonObject? jsonObject, bool usePathAsKey = false, string? separator = null)
        {
            if (usePathAsKey)
            {
                return SerializerEngine.ConvertJsonToDictionary(jsonObject, separator) as IDictionary<string, object>;

            }
            var result = new Dictionary<string, object>();
            if (jsonObject == null) return result;
            foreach (KeyValuePair<string, JsonNode> kvJson in jsonObject)
            {
                result.AddOrUpdate(kvJson.Key, kvJson.Value);
            }

            return result;
        }
        public static void Populate<T>(this JsonNode value, T target) where T : class
        {
            // Deserialize the JsonObject into an object of the same type
            T? updatedObject = JsonSerializer.Deserialize<T>(value.ToString());

            // Get the type of the object
            Type type = typeof(T);

            // Get the properties of the object
            PropertyInfo[] properties = type.GetProperties();

            // Iterate over each property
            foreach (PropertyInfo property in properties)
            {
                // If the property can be read and written to
                if (property is { CanRead: true, CanWrite: true })
                {
                    // Get the value of the property in the updated object
                    object? updatedValue = property.GetValue(updatedObject);

                    // Set the value of the property in the original object to the updated value
                    property.SetValue(target, updatedValue);
                }
            }
        }

        public static JsonSerializerOptions? JsonOptions { get; } = SerializerEngine.JsonSerializationSettings;

        public static string Serialize(this JsonObject json)
        {
            return json.ToString();
        }

        public static string Serialize<T>(this T obj)
        {
            return JsonSerializer.Serialize(obj, JsonOptions);
        }
    }
}
