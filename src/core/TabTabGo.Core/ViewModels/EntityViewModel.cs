using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.ViewModels;

/// <summary>
/// Base view model for entities: audit fields and extra properties.
/// </summary>
public class EntityViewModel
{
    /// <summary>User who created the entity.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Creation date.</summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>User who last updated the entity.</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>Last update date.</summary>
    public DateTime UpdatedDate { get; set; }

    /// <summary>Extra properties flattened into the JSON object.</summary>
    [JsonExtensionData]
    [Nj.JsonExtensionData]
    public IDictionary<string, object?> ExtraProperties { get; set; } = new Dictionary<string, object?>();
}
