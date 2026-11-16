using TabTabGo.Core.Extensions;
using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Entities;

/// <summary>
/// Base class of every TabTabGo entity.
/// </summary>
public class Entity : IEntity
{
    /// <summary>
    /// Date and time when entity created
    /// </summary>
    [IgnoreCopy]
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Date and time when entity updated
    /// </summary>
    [IgnoreCopy]
    public DateTimeOffset UpdatedDate { get; set; }

    /// <summary>
    /// User who created the entity
    /// </summary>
    [IgnoreCopy]
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Last user who updated entity
    /// </summary>
    [IgnoreCopy]
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Whether the entity is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Properties that are not declared on the entity. Flattened into the JSON object by both
    /// System.Text.Json and Newtonsoft.Json.
    /// </summary>
    [JsonExtensionData]
    [Nj.JsonExtensionData]
    public IDictionary<string, object?> ExtraProperties { get; set; } = new Dictionary<string, object?>();
}
