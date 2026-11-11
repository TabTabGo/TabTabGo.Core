using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Data.EF.Tests;

public class UnitOfWorkTests : IDisposable
{
    private readonly SqliteFixture _db = new();
    private readonly TestDbContext _context;
    private readonly TestUnitOfWork _uow;

    public UnitOfWorkTests()
    {
        _db.Seed();
        _context = _db.CreateContext();
        _uow = new TestUnitOfWork(_context);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void Ctor_NullContext_Throws()
    {
        // a null context used to turn every member into a silent no-op (D-21)
        Assert.Throws<ArgumentNullException>(() => new TestUnitOfWork(null!));
        Assert.Throws<ArgumentNullException>(() => new Sut(_context, null!));
    }

    [Fact]
    public async Task SaveChanges_AndAsync_PersistRepositoryChanges()
    {
        _uow.Repository<Product, int>().Insert(new Product { Name = "a" });
        Assert.Equal(1, _uow.SaveChanges());
        await _uow.Repository<Product, int>().InsertAsync(new Product { Name = "b" }, Ct);
        Assert.Equal(1, await _uow.SaveChangesAsync(Ct));
        await _uow.Repository<Product, int>().InsertAsync(new Product { Name = "c" }, Ct);
        Assert.Equal(1, await _uow.SaveChangesInBulkAsync(Ct));

        Assert.Equal(28, await _db.CreateContext().Products.CountAsync(Ct));
    }

    [Fact]
    public void Repository_SameEntityAndKey_ReturnsSameInstance_ConcurrentCallsToo()
    {
        var first = _uow.Repository<Product, int>();

        Assert.Same(first, _uow.Repository<Product, int>());
        Assert.NotSame(first, _uow.Repository<Product, int>("other"));
        Assert.NotSame((object)first, _uow.Repository<Product, long>()); // the key type is part of the identity

        var results = new System.Collections.Concurrent.ConcurrentBag<object>();
        Parallel.For(0, 50, _ => results.Add(_uow.Repository<Category, int>()));
        Assert.Single(results.Distinct());
    }

    [Fact]
    public async Task Transaction_Commit_SavesAndEnds()
    {
        Assert.False(_uow.HasActiveTransaction);
        _uow.BeginTransaction(IsolationLevel.Serializable);
        Assert.True(_uow.HasActiveTransaction);
        _context.Products.Add(new Product { Name = "t" });

        _uow.Commit();

        Assert.False(_uow.HasActiveTransaction);
        Assert.Equal(26, await _db.CreateContext().Products.CountAsync(Ct));
    }

    [Fact]
    public async Task Transaction_Rollback_DiscardsSavedWork()
    {
        _uow.BeginTransaction();
        _context.Products.Add(new Product { Name = "t" });
        _uow.SaveChanges();

        _uow.Rollback();

        Assert.Equal(25, await _db.CreateContext().Products.CountAsync(Ct));
    }

    [Fact]
    public async Task TransactionAsync_BeginCommitRollback()
    {
        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);
        Assert.True(_uow.HasActiveTransaction);
        _context.Products.Add(new Product { Name = "t" });
        await _uow.CommitAsync(Ct);
        Assert.Equal(26, await _db.CreateContext().Products.CountAsync(Ct));

        await _uow.BeginTransactionAsync(cancellationToken: Ct);
        _context.Products.Add(new Product { Name = "u" });
        await _context.SaveChangesAsync(Ct);
        await _uow.RollbackAsync(Ct);
        Assert.Equal(26, await _db.CreateContext().Products.CountAsync(Ct));
    }

    [Fact]
    public void GetChanges_SnapshotNotLive()
    {
        var product = _context.Products.First(p => p.Id == 1);
        product.Name = "first";
        var changes = _uow.GetChanges();
        product.Name = "second";

        var change = Assert.Single(changes);
        Assert.Equal("Modified", change.State);
        Assert.Same(product, change.Entity);
        Assert.Equal("Product 01", change.OriginalValues[nameof(Product.Name)]);
        Assert.Equal("first", change.CurrentValues[nameof(Product.Name)]); // still "first" after the entity moved on
    }

    [Fact]
    public void GetChanges_AddedAndDeleted_CarryTheRightSide()
    {
        _context.Products.Add(new Product { Name = "new" });
        _context.Products.Remove(_context.Products.First(p => p.Id == 2));

        var changes = _uow.GetChanges();

        var added = changes.Single(c => c.State == "Added");
        Assert.Empty(added.OriginalValues);
        Assert.Equal("new", added.CurrentValues[nameof(Product.Name)]);
        var deleted = changes.Single(c => c.State == "Deleted");
        Assert.Empty(deleted.CurrentValues);
        Assert.Equal("Product 02", deleted.OriginalValues[nameof(Product.Name)]);
    }

    [Fact]
    public void GetChanges_NothingChanged_IsEmpty()
    {
        _ = _context.Products.First();

        Assert.Empty(_uow.GetChanges());
    }

    [Fact]
    public async Task AddOrUpdateGraph_NewIsAdded_ExistingIsModified_TrackedIsSkipped()
    {
        var existing = new Product { Id = 3, Name = "graph", Tenant = "A" };
        var fresh = new Product { Name = "fresh" };
        var tracked = _context.Products.First(p => p.Id == 4);

        _uow.AddOrUpdateGraph(existing);
        _uow.AddOrUpdateGraph(fresh);
        _uow.AddOrUpdateGraph(tracked);

        Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Modified, _context.Entry(existing).State);
        Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Added, _context.Entry(fresh).State);
        Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Unchanged, _context.Entry(tracked).State);
        await _uow.SaveChangesAsync(Ct);
    }

    [Fact]
    public void UpdateState_SetsEntityState_NullEntityIgnored()
    {
        var product = _context.Products.First();

        _uow.UpdateState(product, EntityState.Deleted);
        _uow.UpdateState<Product?>(null, EntityState.Deleted);

        Assert.Equal(EntityState.Deleted, _context.Entry(product).State);
    }

    [Fact]
    public void SetEntityStateModified_MarksReferenceModified()
    {
        var product = _context.Products.Include(p => p.Category).First();

        _uow.SetEntityStateModified(product, p => p.Category!);

        Assert.True(_context.Entry(product).Reference(p => p.Category).IsModified);
    }

    [Fact]
    public async Task RemoveNavigationProperty_RemovesDependentsOfTheOwner()
    {
        _context.OrderLines.AddRange(new OrderLine { ProductId = 1, Quantity = 1 }, new OrderLine { ProductId = 1, Quantity = 2 }, new OrderLine { ProductId = 2, Quantity = 3 });
        await _context.SaveChangesAsync(Ct);

        _uow.RemoveNavigationProperty<OrderLine, Product>(new Product { Id = 1 }, 1);
        await _context.SaveChangesAsync(Ct);

        Assert.Equal(1, await _context.OrderLines.CountAsync(Ct));
    }

    [Fact]
    public void RemoveNavigationProperty_BadMapping_LogsAndRethrows()
    {
        var logger = new FakeLogger<UnitOfWork>();
        var uow = new Sut(_context, logger);

        // OrderLine has no "CategoryId" property
        Assert.Throws<InvalidOperationException>(() => uow.RemoveNavigationProperty<OrderLine, Category>(new Category(), 1));
        Assert.Throws<FormatException>(() => uow.RemoveNavigationProperty<OrderLine, Product>(new Product(), "not a number"));

        Assert.Equal(2, logger.Collector.GetSnapshot().Count(r => r.Level == Microsoft.Extensions.Logging.LogLevel.Error));
    }

    [Fact]
    public void Dispose_DoesNotDisposeContextItDoesNotOwn()
    {
        _uow.Dispose();

        Assert.True(_context.Products.Any()); // still usable: the DI container owns it (B13)
    }

    [Fact]
    public void Dispose_OwnedContext_IsClosedAndDisposed()
    {
        var context = _db.CreateContext();
        _ = context.Products.Count(); // opens the connection
        var uow = new TestUnitOfWork(context, ownsContext: true);

        uow.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.Products.Count());
    }

    [Fact]
    public async Task SetOverrides_SaveChangesInBulk_CanBeReplaced()
    {
        var uow = new BulkUow(_context);

        Assert.Equal(-1, await uow.SaveChangesInBulkAsync(Ct));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Success_CommitsTheWork()
    {
        var result = await _uow.ExecuteInTransactionAsync(async ct =>
        {
            _context.Products.Add(new Product { Name = "tx-added", Tenant = "A" });
            await _uow.SaveChangesAsync(ct);
            Assert.True(_uow.HasActiveTransaction);
            return 7;
        }, Ct);

        Assert.Equal(7, result);
        Assert.False(_uow.HasActiveTransaction);
        await using var fresh = _db.CreateContext();
        Assert.Equal(1, fresh.Products.Count(p => p.Name == "tx-added"));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Failure_RollsBackAndRethrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _uow.ExecuteInTransactionAsync<int>(async ct =>
        {
            _context.Products.Add(new Product { Name = "tx-failed", Tenant = "A" });
            await _uow.SaveChangesAsync(ct);
            throw new InvalidOperationException("boom");
        }, Ct));

        Assert.False(_uow.HasActiveTransaction);
        await using var fresh = _db.CreateContext();
        Assert.Equal(0, fresh.Products.Count(p => p.Name == "tx-failed"));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_InsideAnOpenTransaction_LeavesTheCommitToTheOwner()
    {
        await _uow.BeginTransactionAsync(cancellationToken: Ct);

        await _uow.ExecuteInTransactionAsync(ct =>
        {
            _context.Products.Add(new Product { Name = "tx-outer", Tenant = "A" });
            return _uow.SaveChangesAsync(ct);
        }, Ct);

        Assert.True(_uow.HasActiveTransaction);
        await _uow.RollbackAsync(Ct);
        await using var fresh = _db.CreateContext();
        Assert.Equal(0, fresh.Products.Count(p => p.Name == "tx-outer"));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NullOperation_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _uow.ExecuteInTransactionAsync<int>(null!, Ct));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_TransientFailure_RetriesTheWholeUnitAndCommitsOnce()
    {
        using var db = new SqliteFixture(retrying: true);
        db.Seed();
        await using var context = db.CreateContext();
        var uow = new TestUnitOfWork(context);
        var attempts = 0;

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            attempts++;
            if (attempts == 2)
            {
                Assert.Empty(context.ChangeTracker.Entries()); // the failed attempt's tracked state is discarded before the retry
            }

            context.Products.Add(new Product { Name = "retried", Tenant = "A" });
            await uow.SaveChangesAsync(ct);
            if (attempts == 1)
            {
                throw new TransientTestException();
            }

            return attempts;
        }, Ct);

        Assert.Equal(2, attempts);
        await using var fresh = db.CreateContext();
        // the first attempt was rolled back, so the row exists once
        Assert.Equal(1, fresh.Products.Count(p => p.Name == "retried"));
    }

    private sealed class Sut(DbContext context, Microsoft.Extensions.Logging.ILogger<UnitOfWork> logger) : UnitOfWork(context, logger);

    private sealed class BulkUow(DbContext context) : UnitOfWork(context, NullLogger<UnitOfWork>.Instance)
    {
        public override Task<int> SaveChangesInBulkAsync(CancellationToken cancellationToken = default) => Task.FromResult(-1);
    }
}
