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

    public DbSet<Widget> Widgets => Set<Widget>();

    /// <summary>Gives a modified <see cref="Widget"/> a new concurrency token on save, as a rowversion column does.</summary>
    public bool BumpVersion { get; set; }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (BumpVersion)
        {
            foreach (var entry in ChangeTracker.Entries<Widget>().Where(e => e.State == EntityState.Modified))
            {
                entry.Entity.Version = Guid.NewGuid();
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

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
        modelBuilder.Entity<Widget>(b =>
        {
            b.HasKey(w => w.Id);
            b.EntityBuilder();
            b.Property<string>("Region");
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
    : UnitOfWork(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitOfWork>.Instance, ownsContext)
{
    /// <summary>The protected context, for tests.</summary>
    public DbContext ExposedContext => Context;
}

/// <summary>Records the SQL of every command a context runs.</summary>
public class CommandCapture : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    public List<string> Commands { get; } = new();

    public IEnumerable<string> Selects => Commands.Where(c => c.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));

    public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result)
    {
        Commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// Throws <see cref="TransientTestException"/> once, on the first read that follows a write: after SaveChanges and before the commit.
/// </summary>
public sealed class FailOnceAfterWrite : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    private bool _wrote;

    /// <summary>How many times the failure was raised.</summary>
    public int Failures { get; private set; }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Mark(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Mark(command);
        if (_wrote && Failures == 0 && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            Failures++;
            throw new TransientTestException();
        }

        return ValueTask.FromResult(result);
    }

    // SQLite runs UPDATE ... RETURNING through the reader
    private void Mark(System.Data.Common.DbCommand command)
    {
        var text = command.CommandText.TrimStart();
        if (text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) || text.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            _wrote = true;
        }
    }
}

/// <summary>The transient failure <see cref="RetryingStrategy"/> retries on.</summary>
public sealed class TransientTestException() : Exception("transient");

/// <summary>A retrying execution strategy for tests (retries <see cref="TransientTestException"/> immediately, up to 3 times).</summary>
public sealed class RetryingStrategy(Microsoft.EntityFrameworkCore.Storage.ExecutionStrategyDependencies dependencies, Action? onRetry = null)
    : Microsoft.EntityFrameworkCore.Storage.ExecutionStrategy(dependencies, 3, TimeSpan.Zero)
{
    protected override void OnRetry()
    {
        onRetry?.Invoke();
        base.OnRetry();
    }

    protected override bool ShouldRetryOn(Exception exception) => exception is TransientTestException;
}
