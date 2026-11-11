using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.ViewModels;

/// <summary>
/// A reference to another record: id and display text.
/// </summary>
/// <typeparam name="TIdType">Id type</typeparam>
public class ReferenceViewModel<TIdType>
{
    /// <summary>Id of the referenced record.</summary>
    public TIdType Id { get; set; } = default!; // justified: generic id, default is the unset value

    /// <summary>Display text.</summary>
    public string Display { get; set; } = string.Empty;

    /// <summary>Extra properties flattened into the JSON object.</summary>
    [JsonExtensionData]
    [Nj.JsonExtensionData]
    public IDictionary<string, object?> Properties { get; set; } = new Dictionary<string, object?>();
}
