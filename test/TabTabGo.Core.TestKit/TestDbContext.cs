using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using TabTabGo.Data.EF;
using TabTabGo.Data.EF.Extensions;

namespace TabTabGo.Core.TestKit;

/// <summary>The model every relational test runs on.</summary>
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<CompositeKeyEntity> Composites => Set<CompositeKeyEntity>();

    public DbSet<GuidEntity> Guids => Set<GuidEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot order or compare DateTimeOffset; store it as a number
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.EntityBuilder();
            b.HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.ProductId);
        });
        modelBuilder.Entity<Category>(b =>
        {
            b.HasKey(c => c.Id);
            b.EntityBuilder();
            b.HasMany(c => c.Products).WithOne(p => p.Category).HasForeignKey(p => p.CategoryId);
        });
        modelBuilder.Entity<OrderLine>(b =>
        {
            b.HasKey(l => l.Id);
            b.EntityBuilder();
        });
        modelBuilder.Entity<CompositeKeyEntity>(b =>
        {
            b.HasKey(c => new { c.FirstId, c.SecondId });
            b.EntityBuilder();
        });
        modelBuilder.Entity<GuidEntity>(b =>
        {
            b.HasKey(g => g.Id);
            b.EntityBuilder();
        });
    }
}

/// <summary>Unit of work over <see cref="TestDbContext"/>.</summary>
public class TestUnitOfWork(DbContext context, bool ownsContext = false)
    : UnitOfWork(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitOfWork>.Instance, ownsContext);
