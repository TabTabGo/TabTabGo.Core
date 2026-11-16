namespace TabTabGo.Core.Entities;

/// <summary>
/// The TabTabGo entity contract: audit fields, an enabled flag and a bag for properties the entity type does not declare.
/// </summary>
public interface IEntity
{
    /// <summary>User who created the entity.</summary>
    string? CreatedBy { get; set; }

    /// <summary>Date and time the entity was created.</summary>
    DateTimeOffset CreatedDate { get; set; }

    /// <summary>Whether the entity is enabled.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Properties that are not declared on the entity type.</summary>
    IDictionary<string, object?> ExtraProperties { get; set; }

    /// <summary>Last user who updated the entity.</summary>
    string? UpdatedBy { get; set; }

    /// <summary>Date and time the entity was last updated.</summary>
    DateTimeOffset UpdatedDate { get; set; }
}
