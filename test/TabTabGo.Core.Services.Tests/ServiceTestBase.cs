using Microsoft.Extensions.Logging.Abstractions;
using TabTabGo.Core.Data;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Core.Services.Tests;

/// <summary>Gives a test class a seeded SQLite database, a unit of work and a product service.</summary>
public abstract class ServiceTestBase : IDisposable
{
    protected ServiceTestBase(bool seed = true)
    {
        Db = new SqliteFixture();
        if (seed)
        {
            Db.Seed();
        }

        Context = Db.CreateContext();
        UnitOfWork = new TestUnitOfWork(Context);
        Service = new TestProductService(UnitOfWork, NullLogger<BaseService<Product, int>>.Instance);
    }

    protected SqliteFixture Db { get; }

    protected TestDbContext Context { get; }

    protected IUnitOfWork UnitOfWork { get; }

    protected TestProductService Service { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Context.Dispose();
        Db.Dispose();
        GC.SuppressFinalize(this);
    }
}
