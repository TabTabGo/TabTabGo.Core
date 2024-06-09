using Microsoft.EntityFrameworkCore;

namespace TabTabGo.Data.EF;

public interface IEntityTypeConfiguration
{
    void ApplyConfiguration(ModelBuilder modelBuilder);
}