using TabTabGo.Core.TestKit;
using TabTabGo.Data.EF.Repositories;

namespace TabTabGo.Data.EF.Tests;

public abstract class RepositoryTestBase : IDisposable
{
    protected RepositoryTestBase(bool seed = true)
    {
        Db = new SqliteFixture();
        if (seed)
        {
            Db.Seed();
        }

        Context = Db.CreateContext();
        Repo = new GenericRepository<Product, int>(Context);
    }

    protected SqliteFixture Db { get; }

    protected TestDbContext Context { get; }

    protected GenericRepository<Product, int> Repo { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Context.Dispose();
        Db.Dispose();
        GC.SuppressFinalize(this);
    }
}
