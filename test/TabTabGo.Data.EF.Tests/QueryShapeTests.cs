using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TabTabGo.Core.Enums;
using TabTabGo.Core.TestKit;
using TabTabGo.Data.EF.Repositories;

namespace TabTabGo.Data.EF.Tests;

/// <summary>Tests that look at the SQL the repositories produce.</summary>
public sealed class QueryShapeTests : IDisposable
{
    private readonly SqliteFixture _db = new();
    private readonly CommandCapture _capture = new();
    private readonly TestDbContext _context;
    private readonly GenericRepository<Product, int> _repo;

    public QueryShapeTests()
    {
        _db.Seed();
        _context = _db.CreateContext(_capture);
        _repo = new GenericRepository<Product, int>(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- paging without an order falls back to the primary key (Q1c) ----------------------------------------------------

    [Fact]
    public async Task GetPageListAsync_NoOrderBy_OrdersByThePrimaryKeyInSql()
    {
        await _repo.GetPageListAsync(p => p, pageNumber: 1, pageSize: 5, cancellationToken: Ct);

        var page = _capture.Selects.Last();
        Assert.Contains("ORDER BY \"p\".\"Id\"", page);
        Assert.Contains("LIMIT", page);
    }

    [Fact]
    public async Task GetPageListAsync_NoOrderBy_CompositeKeyOrdersByEveryKeyColumn()
    {
        var repo = new GenericReadRepository<CompositeKeyEntity, int>(_context);

        await repo.GetPageListAsync(c => c, pageSize: 5, cancellationToken: Ct);

        Assert.Contains("ORDER BY \"c\".\"FirstId\", \"c\".\"SecondId\"", _capture.Selects.Last());
    }

    [Fact]
    public async Task GetPageListAsync_WithOrderBy_DoesNotAddTheKeyOrder()
    {
        await _repo.GetPageListAsync(p => p, orderBy: q => q.OrderBy(p => p.Price), pageSize: 5, cancellationToken: Ct);

        var sql = _capture.Selects.Last();
        Assert.Contains("ORDER BY \"p\".\"Price\"", sql);
        Assert.DoesNotContain("\"p\".\"Id\"", sql.Substring(sql.IndexOf("ORDER BY", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetProjectedPageListAsync_NoOrderBy_OrdersByThePrimaryKeyInSql()
    {
        await _repo.GetProjectedPageListAsync(p => new ProductViewModel { Id = p.Id }, pageSize: 5, cancellationToken: Ct);

        Assert.Contains("ORDER BY \"p\".\"Id\"", _capture.Selects.Last());
    }

    [Fact]
    public void Get_SkipTakeWithoutOrderBy_OrdersByThePrimaryKeyInSql()
    {
        _ = _repo.Get(rowsToSkip: 2, rowsToTake: 3).ToList();

        Assert.Contains("ORDER BY \"p\".\"Id\"", _capture.Selects.Last());
    }

    [Fact]
    public void ApplyKeyOrder_AddsThePrimaryKeyOrder()
    {
        var sql = _repo.ApplyKeyOrder(_context.Products).ToQueryString();

        Assert.Contains("ORDER BY \"p\".\"Id\"", sql);
        Assert.Throws<ArgumentNullException>(() => _repo.ApplyKeyOrder(null!));
    }

    // --- tracking flag (R3) ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPageListAsync_EnableTracking_TracksTheEntities()
    {
        await _repo.GetPageListAsync(p => p, pageSize: 3, cancellationToken: Ct);
        Assert.Empty(_context.ChangeTracker.Entries()); // untracked by default

        await _repo.GetPageListAsync(p => p, pageSize: 3, flags: QueryFlags.EnableTracking, cancellationToken: Ct);

        Assert.Equal(3, _context.ChangeTracker.Entries<Product>().Count());
    }

    [Fact]
    public void EnableTracking_WinsOverDisableTracking_InSetFlags()
    {
        _ = _repo.SetFlags(_context.Products, QueryFlags.DisableTracking | QueryFlags.EnableTracking).ToList();

        Assert.Equal(25, _context.ChangeTracker.Entries<Product>().Count());
    }

    // --- materialise once (N-10) ----------------------------------------------------------------------------------------

    [Fact]
    public void Update_JsonObjectByFilter_RunsOneSelect()
    {
        _repo.Update(new JsonObject { ["description"] = "x" }, p => p.Id <= 3);

        Assert.Single(_capture.Selects); // the query was enumerated twice before (N-10)
    }

    [Fact]
    public void Delete_ByFilterAndByQueryable_RunOneSelectEach()
    {
        _repo.Delete(p => p.Id <= 2);
        Assert.Single(_capture.Selects);

        _capture.Commands.Clear();
        _repo.Delete(_context.Products.Where(p => p.Id == 10));
        Assert.Single(_capture.Selects);
    }

    // --- key predicates (R4, R7) ----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetKeysPredicate_IsOneInList_NotATreeOfOrs()
    {
        var predicate = _repo.GetKeysPredicate(new[] { 1, 2, 3, 3 })!;

        var found = await _context.Products.Where(predicate).ToListAsync(Ct);

        Assert.Equal(new[] { 1, 2, 3 }, found.Select(p => p.Id).OrderBy(i => i));
        var sql = _capture.Selects.Last();
        Assert.Contains(" IN (", sql);
        Assert.DoesNotContain(" OR ", sql);
    }

    [Fact]
    public void GetKeysPredicate_KeyTypeThatDoesNotMatchTheColumn_IsNull_AndCompositeThrows()
    {
        var longRepo = new GenericReadRepository<Product, long>(_context);
        var composite = new GenericReadRepository<CompositeKeyEntity, int>(_context);

        Assert.Null(longRepo.GetKeysPredicate(new long[] { 1 }));
        Assert.Throws<NotSupportedException>(() => composite.GetKeysPredicate(new[] { 1 }));
        Assert.Throws<ArgumentNullException>(() => _repo.GetKeysPredicate(null!));
    }

    [Fact]
    public void UnitOfWork_ExposesItsContext()
    {
        var uow = new TestUnitOfWork(_context);

        Assert.Same(_context, uow.ExposedContext);
    }
}
