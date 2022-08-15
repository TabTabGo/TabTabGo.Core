using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace TabTabGo.Core.Extensions
{
    public static class JsonExtensions
    {
        public static string ConvertToXml(this string json)
        {
            return ConvertToXml(json, "root");
        }
        public static string ConvertToXml(this string json, string rootFieldName)
        {
            var xml = JsonConvert.DeserializeXmlNode(json, rootFieldName, true);
            return xml?.OuterXml;
        }
        public static string ConvertToXml(this JToken jToken)
        {
            return ConvertToXml(jToken, "root");
        }
        public static string ConvertToXml(this JToken jToken , string rootFieldName)
        {
            string sToken;
            if(jToken is JArray )
            {
                var jObject = new JObject() { { rootFieldName, jToken } };
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
        public static IDictionary<string, object> ConvertToDictionary(this JObject? jsonObject, bool usePathAsKey = false, string? separator = null)
        {
            if (usePathAsKey)
            {
                return SerializerEngine.ConvertJsonToDictionary(jsonObject, separator) as IDictionary<string, object>;

            }
            var result = new Dictionary<string, object>();
            if (jsonObject == null) return result;
            foreach (KeyValuePair<string, JToken> kvJson in jsonObject)
            {
                result.AddOrUpdate(kvJson.Key, kvJson.Value);
            }

            return result;
        }
        public static void Populate<T>(this JToken value, T target) where T : class
        {
            using (var sr = value.CreateReader())
            {
                JsonSerializer.CreateDefault().Populate(sr, target); // Uses the system default JsonSerializerSettings
            }
        }
        
        public static JsonSerializerSettings? JsonOptions { get; } = new JsonSerializerSettings()
        {
            // Add Camel case to the JSON output
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            // convert Enum to string
            Converters = new List<JsonConverter> {new StringEnumConverter()}
        };

        public static string Serialize(this JObject json)
        {
            return json.ToString();
        }

        public static string Serialize<T>(this T obj)
        {
            return JsonConvert.SerializeObject(obj, JsonOptions);
        }
    }
}
