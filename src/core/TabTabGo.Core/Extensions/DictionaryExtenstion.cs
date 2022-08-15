namespace TabTabGo.Core.Extensions
{

    public static class DictionaryExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        ///  <see cref="https://stackoverflow.com/questions/3928822/comparing-2-dictionarystring-string-instances"/>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        public static bool DictionaryEqual<TKey, TValue>(
            this IDictionary<TKey, TValue> first, IDictionary<TKey, TValue> second)
        {
            return first.DictionaryEqual(second, null);
        }

        public static bool DictionaryEqual<TKey, TValue>(
            this IDictionary<TKey, TValue> first, IDictionary<TKey, TValue> second,
            IEqualityComparer<TValue> valueComparer)
        {
            if (first == second) return true;
            if ((first == null) || (second == null)) return false;
            if (first.Count != second.Count) return false;

            valueComparer = valueComparer ?? EqualityComparer<TValue>.Default;

            foreach (var kvp in first)
            {
                TValue secondValue;
                if (!second.TryGetValue(kvp.Key, out secondValue)) return false;
                if (!valueComparer.Equals(kvp.Value, secondValue)) return false;
            }
            return true;
        }

        public static void AddOrUpdate<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key] = value;
            }
            else
            {
                dictionary.Add(key, value);
            }
        }


        public static string ConvertToXml(this IDictionary<string, object>? dictionary, bool usePathAsKey = false, char[]? separator = null)
        {
            if (dictionary == null) return string.Empty ;
            var jsonObject = usePathAsKey ? SerializerEngine.ConvertDictionaryToJson(dictionary.ToDictionary(d => d.Key, d => d.Value.ToString()), separator)
             : JToken.Parse(JsonConvert.SerializeObject(dictionary));

            return jsonObject.ConvertToXml();
        }

        public static JToken ConvertToJson(this IDictionary<string, object>? dictionary, bool usePathAsKey = false, char[]? separator = null)
        {
            if (dictionary == null) return new JObject();
            return usePathAsKey ? SerializerEngine.ConvertDictionaryToJson(dictionary.ToDictionary(d => d.Key, d => d.Value.ToString()), separator)
             : JToken.Parse(JsonConvert.SerializeObject(dictionary));
        }
        
        

    }
}
