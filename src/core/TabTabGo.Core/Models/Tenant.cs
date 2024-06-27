
namespace TabTabGo.Core
{
    public abstract class Tenant<TKey> where TKey : struct
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public TKey Id { get; set; }

        public dynamic Settings { get; set; }
        [JsonExtensionData]
        public IDictionary<string, object> ExtraProperties { get; set; } = new Dictionary<string, object>();
    }
}
