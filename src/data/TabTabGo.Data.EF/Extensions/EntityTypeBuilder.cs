using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TabTabGo.Core;
using TabTabGo.Core.Entities;

namespace TabTabGo.Data.EF.Extensions;

/// <summary>
/// EF model-building helpers for TabTabGo entities.
/// </summary>
public static class EntityTypeBuilder
{
    /// <summary>
    /// The options used to store <see cref="IEntity.ExtraProperties"/>: compact, null values kept, Newtonsoft tokens supported.
    /// </summary>
    private static readonly JsonSerializerOptions StorageOptions = CreateStorageOptions();

    private static JsonSerializerOptions CreateStorageOptions()
    {
        var options = SerializerEngine.CreateJsonSerializerOptions();
        options.WriteIndented = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        return options;
    }

    /// <summary>
    /// Configures the audit columns and stores <see cref="IEntity.ExtraProperties"/> as compact JSON, with a value
    /// comparer so that in-place edits of the dictionary are detected and saved.
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    /// <param name="entityTypeBuilder">The builder</param>
    public static EntityTypeBuilder<TEntity> EntityBuilder<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder) where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        entityTypeBuilder.Property(p => p.CreatedBy).HasMaxLength(50);
        entityTypeBuilder.Property(p => p.UpdatedBy).HasMaxLength(50);
        entityTypeBuilder.Property(p => p.ExtraProperties)
            .HasConversion(
                e => Serialize(e),
                e => Deserialize(e),
                new ValueComparer<IDictionary<string, object?>>(
                    (a, b) => Serialize(a) == Serialize(b),
                    a => Serialize(a).GetHashCode(),
                    a => Deserialize(Serialize(a))));
        return entityTypeBuilder;
    }

    private static string Serialize(IDictionary<string, object?>? value) =>
        JsonSerializer.Serialize(value ?? new Dictionary<string, object?>(), StorageOptions);

    private static IDictionary<string, object?> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(json, StorageOptions) ?? new Dictionary<string, object?>();
}
