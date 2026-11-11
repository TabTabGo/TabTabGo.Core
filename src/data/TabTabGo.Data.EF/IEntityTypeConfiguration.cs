using Microsoft.EntityFrameworkCore;

namespace TabTabGo.Data.EF;

/// <summary>
/// A group of entity configurations that can be applied to a model.
/// </summary>
public interface IEntityTypeConfiguration
{
    /// <summary>Applies the configurations to the model.</summary>
    /// <param name="modelBuilder">The model builder</param>
    void ApplyConfiguration(ModelBuilder modelBuilder);
}
