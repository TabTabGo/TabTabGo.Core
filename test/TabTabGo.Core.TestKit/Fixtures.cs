using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TabTabGo.Core.TestKit;

/// <summary>
/// A private in-memory SQLite database for one test class instance. The connection stays open for the life of the
/// fixture, so every context created from it sees the same data.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    private readonly bool _retrying;

    /// <param name="retrying">
    /// Registers a retrying execution strategy, like SQL Server or Npgsql do in production: a transaction opened by hand
    /// outside <c>CreateExecutionStrategy().ExecuteAsync</c> then throws.
    /// </param>
    public SqliteFixture(bool retrying = false)
    {
        _retrying = retrying;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public SqliteConnection Connection => _connection;

    /// <summary>Runs between a failed attempt and its retry (a concurrent writer committing in that window).</summary>
    public Action? OnRetry { get; set; }

    /// <summary>Gives modified widgets a new concurrency token on save.</summary>
    public bool BumpVersion { get; set; }

    /// <summary>Adds one widget (tenant A, with a note, a token and a shadow column) and returns nothing; its id is 1.</summary>
    public void SeedWidget()
    {
        using var context = CreateContext();
        var widget = new Widget { Name = "w1", Tenant = "A", Note = "orig", Version = Guid.NewGuid() };
        context.Widgets.Add(widget);
        context.Entry(widget).Property("Region").CurrentValue = "EU";
        context.SaveChanges();
    }

    /// <summary>Adds one gadget (tenant A, owned address "old", no lines); its id is 1.</summary>
    public void SeedGadget()
    {
        using var context = CreateContext();
        context.Gadgets.Add(new Gadget { Name = "g", Address = new GadgetAddress { City = "old" } });
        context.SaveChanges();
    }

    /// <summary>Interceptors added to every context this fixture creates (set before the first use).</summary>
    public List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor> DefaultInterceptors { get; } = new();

    public TestDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection, o =>
            {
                if (_retrying)
                {
                    o.ExecutionStrategy(d => new RetryingStrategy(d, () => OnRetry?.Invoke()));
                }
            });
        var all = DefaultInterceptors.Concat(interceptors).ToArray();
        if (all.Length > 0)
        {
            builder.AddInterceptors(all);
        }

        return new TestDbContext(builder.Options) { BumpVersion = BumpVersion };
    }

    /// <summary>Adds a few products, categories and lines.</summary>
    public void Seed()
    {
        using var context = CreateContext();
        var category = new Category { Name = "Hardware" };
        context.Categories.Add(category);
        context.SaveChanges();
        for (var i = 1; i <= 25; i++)
        {
            context.Products.Add(new Product
            {
                Name = $"Product {i:00}",
                Price = i * 10,
                Tenant = i % 2 == 0 ? "A" : "B",
                CategoryId = category.Id,
            });
        }

        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>A private EF InMemory database for tests that do not need SQL.</summary>
public sealed class InMemoryFixture
{
    private readonly string _name = Guid.NewGuid().ToString();

    public TestDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(_name);
        return new TestDbContext(builder.Options);
    }
}
