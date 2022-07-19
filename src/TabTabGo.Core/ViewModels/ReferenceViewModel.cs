


namespace TabTabGo.Core.ViewModels
{
    public class ReferenceViewModel<TIdType>
    {
        public TIdType Id { get; set; }
        public string Display { get; set; }
        [JsonExtensionData]
        public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }
}
