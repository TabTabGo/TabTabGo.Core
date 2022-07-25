namespace TabTabGo.Core.ViewModels;

public class EntityViewModel
{
    public string CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public string UpdatedBy { get; set; }
    public DateTime UpdatedDate { get; set; }
    [JsonExtensionData]
    public IDictionary<string, object> ExtraProperties { get; set; } = new Dictionary<string, object>();
}