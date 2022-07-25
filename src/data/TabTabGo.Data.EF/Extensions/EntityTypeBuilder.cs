using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
        entityTypeBuilder.Property(p => p.ExtraProperties).HasConversion(e => e.Serialize(),
            e => e.Deserialize<IDictionary<string, object>>() ?? new Dictionary<string, object>() ,
            new ValueComparer<IDictionary<string, object>>(
                (x, y) => x != null && y != null ? x.SequenceEqual(y) : x == null && y == null,
                x => x.GetHashCode()));
        return entityTypeBuilder;
    }
}
