using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TabTabGo.Core;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Extensions;

namespace TabTabGo.Data.EF.Extensions;

public static class EntityTypeBuilder
{
    public static EntityTypeBuilder<TEntity> EntityBuilder<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder) where TEntity : class, IEntity
    {
        entityTypeBuilder.Property(p => p.CreatedBy).HasMaxLength(50);
        entityTypeBuilder.Property(p => p.UpdatedBy).HasMaxLength(50);
        entityTypeBuilder.Property(p => p.ExtraProperties).HasConversion(
            e => JsonSerializer.Serialize(e, SerializerEngine.JsonSerializationSettings),
            e => JsonSerializer.Deserialize<IDictionary<string, object>>(e, SerializerEngine.JsonSerializationSettings) ?? new Dictionary<string, object>());
        return entityTypeBuilder;
    }
}
