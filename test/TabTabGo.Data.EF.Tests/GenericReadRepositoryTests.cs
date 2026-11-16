using System.Data;
using Microsoft.EntityFrameworkCore;
using TabTabGo.Core.Enums;
using TabTabGo.Core.TestKit;
using TabTabGo.Data.EF.Repositories;

namespace TabTabGo.Data.EF.Tests;

public class GenericReadRepositoryTests : RepositoryTestBase
{
    // --- paging (ADR 0004) -------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPageListAsync_FirstPage_IsPageNumberOneWithKeyOrder()
    {
        var page = await Repo.GetPageListAsync(p => p, pageNumber: 0, pageSize: 10, cancellationToken: Ct);

        Assert.Equal(1, page.PageNumber);
        Assert.Equal(25, page.TotalItems);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(Enumerable.Range(1, 10), page.Items.Select(p => p.Id));
        Assert.False(page.HasPreviousPage);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public async Task GetPageListAsync_PageIndexOne_SkipsOnePageAndReportsPageTwo()
    {
        var page = await Repo.GetPageListAsync(p => p, pageNumber: 1, pageSize: 10, cancellationToken: Ct);

        Assert.Equal(2, page.PageNumber);
        Assert.Equal(Enumerable.Range(11, 10), page.Items.Select(p => p.Id));
        Assert.True(page.HasPreviousPage);
    }

    [Fact]
    public async Task GetPageListAsync_LastPage_HasRemainder()
    {
        var page = await Repo.GetPageListAsync(p => p, pageNumber: 2, pageSize: 10, cancellationToken: Ct);

        Assert.Equal(5, page.Count);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task GetPageListAsync_NegativePage_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repo.GetPageListAsync(p => p, pageNumber: -1, cancellationToken: Ct));
    }

    [Fact]
    public async Task GetPageListAsync_FilterOrderIncludeAndFlags_AreApplied()
    {
        var page = await Repo.GetPageListAsync(p => p.Name, p => p.Price > 100, 0, 5, q => q.OrderByDescending(p => p.Price),
            new[] { "Category" }, QueryFlags.DisableTracking, Ct);

        Assert.Equal(15, page.TotalItems);
        Assert.Equal("Product 25", page.Items[0]);
        Assert.Empty(Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetPageListAsync_NullFlags_StillDisablesTracking()
    {
        // DisableTracking | null used to be null and tracked everything (N-5)
        await Repo.GetPageListAsync(p => p, flags: null, cancellationToken: Ct);

        Assert.Empty(Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetPageListAsync_PageSizeZero_ReturnsEverythingWithDefaultReportedSize()
    {
        var page = await Repo.GetPageListAsync(p => p, pageSize: 0, cancellationToken: Ct);

        Assert.Equal(25, page.Count);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task GetPageListAsync_CompositeKey_OrdersByEveryKeyColumn()
    {
        Context.Composites.AddRange(
            new CompositeKeyEntity { FirstId = 2, SecondId = 1, Label = "b" },
            new CompositeKeyEntity { FirstId = 1, SecondId = 2, Label = "a2" },
            new CompositeKeyEntity { FirstId = 1, SecondId = 1, Label = "a1" });
        await Context.SaveChangesAsync(Ct);
        var repo = new GenericReadRepository<CompositeKeyEntity, int>(Context);

        var page = await repo.GetPageListAsync(c => c.Label, cancellationToken: Ct);

        Assert.Equal(new[] { "a1", "a2", "b" }, page.Items);
    }

    [Fact]
    public async Task GetProjectedPageListAsync_ProjectsInTheDatabase()
    {
        var page = await Repo.GetProjectedPageListAsync(p => new ProductViewModel { Id = p.Id, Name = p.Name }, p => p.Id <= 12, 1, 5, cancellationToken: Ct);

        Assert.Equal(12, page.TotalItems);
        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, page.Items.Select(p => p.Id));
        Assert.Equal(2, page.PageNumber);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repo.GetProjectedPageListAsync(p => new ProductViewModel(), pageNumber: -1, cancellationToken: Ct));
    }

    // --- Get / queryable -----------------------------------------------------------------------------------------

    [Fact]
    public void Get_FilterOrderSkipTake_AppliedEvenWithoutOrderBy()
    {
        // take/skip used to be ignored unless an orderBy was given (N-6)
        var items = Repo.Get(rowsToSkip: 5, rowsToTake: 3).ToList();

        Assert.Equal(new[] { 6, 7, 8 }, items.Select(p => p.Id));
    }

    [Fact]
    public void Get_WithOrderBy_SkipAndTake()
    {
        var items = Repo.Get(orderBy: q => q.OrderByDescending(p => p.Id), rowsToSkip: 1, rowsToTake: 2).ToList();

        Assert.Equal(new[] { 24, 23 }, items.Select(p => p.Id));
        Assert.Equal(new[] { 25, 24 }, Repo.Get(orderBy: q => q.OrderByDescending(p => p.Id), rowsToTake: 2).Select(p => p.Id));
        Assert.Equal(new[] { 2, 1 }, Repo.Get(orderBy: q => q.OrderByDescending(p => p.Id), rowsToSkip: 23).Select(p => p.Id));
    }

    [Fact]
    public void GetWithSelector_Projects()
    {
        var names = Repo.Get(p => p.Name, p => p.Id < 3).ToList();

        Assert.Equal(new[] { "Product 01", "Product 02" }, names);
    }

    [Fact]
    public async Task GetAsync_AllOverloads()
    {
        var entities = (await Repo.GetAsync(p => p.Id > 22, cancellationToken: Ct)).ToList();
        var names = (await Repo.GetAsync(p => p.Name, p => p.Id > 22, cancellationToken: Ct)).ToList();

        Assert.Equal(3, entities.Count);
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void GetQueryable_Flags_ControlTrackingAndLocal()
    {
        _ = Repo.GetQueryable(QueryFlags.DisableTracking).ToList();
        Assert.Empty(Context.ChangeTracker.Entries());

        _ = Repo.GetQueryable().Where(p => p.Id == 1).ToList(); // tracked
        var local = Repo.GetQueryable(QueryFlags.UseLocal).ToList();

        Assert.Single(local);
    }

    [Fact]
    public void GetQueryable_Include_LoadsNavigation_DollarPrefixedSkipped()
    {
        var product = Repo.GetQueryable(new[] { "Category", "$ignored" }).First();

        Assert.NotNull(product.Category);
    }

    [Fact]
    public void GetQueryable_Expandable_AcceptsLinqKitPredicates()
    {
        var query = Repo.GetQueryable(QueryFlags.IsExpandable).Where(p => p.Id == 2);

        Assert.Single(query.ToList());
    }

    [Fact]
    public void GetQueryable_GenericSelector_ProjectsAndCasts()
    {
        var all = Repo.GetQueryable<Product>().Count(); // no selector: the entities themselves
        var projected = Repo.GetQueryable<ProductViewModel>(p => new ProductViewModel { Id = p.Id }, p => p.Id == 2).Single();

        Assert.Equal(2, projected.Id);
        Assert.Equal(25, all);
    }

    [Fact]
    public void IncludeProperties_NullAndDollar_ReturnSourceUntouched()
    {
        var source = Context.Products.AsQueryable();

        Assert.Same(source, Repo.IncludeProperties(source, null));
        Assert.NotNull(Repo.IncludeProperties(source, new[] { "$x", "Category" }).First().Category);
    }

    [Fact]
    public void SetFlags_Combines()
    {
        var query = Repo.SetFlags(Context.Products.AsQueryable(), QueryFlags.DisableTracking | QueryFlags.IsExpandable | QueryFlags.IgnoreFixQuery);
        _ = query.ToList();

        Assert.Empty(Context.ChangeTracker.Entries());
        Assert.Same(Context.Products, Repo.SetFlags(Context.Products, null));
    }

    // --- GetByKey ------------------------------------------------------------------------------------------------

    [Fact]
    public void GetByKey_WithIncludes_LoadsNavigation()
    {
        // includes used to be silently dropped (O-5)
        var product = Repo.GetByKey(1, new[] { "Category" });

        Assert.NotNull(product?.Category);
    }

    [Fact]
    public async Task GetByKeyAsync_WithIncludes_LoadsNavigation_NoIncludesUsesFind_MissingIsNull()
    {
        Assert.NotNull((await Repo.GetByKeyAsync(1, new[] { "Category" }, Ct))?.Category);
        Assert.NotNull(await Repo.GetByKeyAsync(2, null, Ct));
        Assert.Null(await Repo.GetByKeyAsync(999, new[] { "Category" }, Ct));
        Assert.Null(Repo.GetByKey(999));
    }

    [Fact]
    public void GetByKey_DollarOnlyIncludes_ActLikeNone()
    {
        Assert.NotNull(Repo.GetByKey(1, new[] { "$x" }));
    }

    [Fact]
    public async Task GetByKey_GuidKey_Works()
    {
        var guid = new GuidEntity { Name = "g" };
        Context.Guids.Add(guid);
        await Context.SaveChangesAsync(Ct);
        var repo = new GenericReadRepository<GuidEntity, Guid>(Context);

        Assert.Equal("g", (await repo.GetByKeyAsync(guid.Id, new[] { "$x" }, Ct))?.Name);
        Assert.Equal("g", repo.GetByKey(guid.Id, new[] { "$none" })?.Name);
    }

    [Fact]
    public void GetByKey_CompositeKey_IsNotSupportedWhenIncludesNeedAPredicate()
    {
        var repo = new GenericReadRepository<CompositeKeyEntity, int>(Context);
        Context.Composites.Add(new CompositeKeyEntity { FirstId = 1, SecondId = 1 });
        Context.SaveChanges();

        Assert.Throws<NotSupportedException>(() => repo.GetByKey(1, new[] { "Anything" }));
    }

    [Fact]
    public void GetByKey_UnmappedEntity_Throws()
    {
        var repo = new GenericReadRepository<TabTabGo.Core.Entities.Entity, int>(Context);

        Assert.ThrowsAny<InvalidOperationException>(() => repo.GetByKey(1, new[] { "x" }));
    }

    // --- FirstOrDefault ------------------------------------------------------------------------------------------

    [Fact]
    public async Task FirstOrDefault_AllOverloads()
    {
        Assert.Equal(1, Repo.FirstOrDefault()?.Id);
        Assert.Equal(25, Repo.FirstOrDefault(orderBy: q => q.OrderByDescending(p => p.Id))?.Id);
        Assert.Equal(3, Repo.FirstOrDefault(rowsToSkip: 2)?.Id);
        Assert.Null(Repo.FirstOrDefault(p => p.Id == 999));
        Assert.Equal("Product 02", Repo.FirstOrDefault(p => p.Name, p => p.Id == 2));
        Assert.Equal(7, (await Repo.FirstOrDefaultAsync(p => p.Id == 7, cancellationToken: Ct))?.Id);
        Assert.Equal("Product 03", await Repo.FirstOrDefaultAsync(p => p.Name, p => p.Id == 3, cancellationToken: Ct));
        Assert.Null(await Repo.FirstOrDefaultAsync(p => p.Id == 999, cancellationToken: Ct));
    }

    // --- Count / Any ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Count_AndAny_WithFilterAndFlags()
    {
        Assert.Equal(25, Repo.Count());
        Assert.Equal(12, Repo.Count(p => p.Tenant == "A"));
        Assert.Equal(12, Repo.Count(p => p.Tenant == "A", new[] { "Category" }, QueryFlags.IsExpandable));
        Assert.Equal(25, await Repo.CountAsync(cancellationToken: Ct));
        Assert.Equal(13, await Repo.CountAsync(p => p.Tenant == "B", flags: QueryFlags.IgnoreFixQuery, cancellationToken: Ct));
        Assert.True(await Repo.AnyAsync(p => p.Id == 1, cancellationToken: Ct));
        Assert.False(await Repo.AnyAsync(p => p.Id == 999, cancellationToken: Ct));
        Assert.True(await Repo.AnyAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task CountAsyncAndToListAsync_OverQueryable_UseTheDatabaseOrFallBackToLinq()
    {
        Assert.Equal(25, await Repo.CountAsync(Context.Products.AsQueryable(), Ct));
        Assert.Equal(25, (await Repo.ToListAsync(Context.Products.AsQueryable(), Ct)).Count);
        Assert.Equal(3, await Repo.CountAsync(new[] { 1, 2, 3 }.AsQueryable(), Ct));
        Assert.Equal(new[] { 1, 2 }, await Repo.ToListAsync(new[] { 1, 2 }.AsQueryable(), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Repo.CountAsync<int>(null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Repo.ToListAsync<int>(null!, Ct));
    }

    // --- misc ---------------------------------------------------------------------------------------------------

    [Fact]
    public void IsModified_ComparesByValueNotReference()
    {
        var product = Repo.GetByKey(1)!;
        Assert.False(Repo.IsModified(product, nameof(Product.Name)));

        product.Name = new string("Product 01".ToCharArray()); // equal value, different reference
        Assert.False(Repo.IsModified(product, nameof(Product.Name)));

        product.Name = "different";
        Assert.True(Repo.IsModified(product, nameof(Product.Name)));
    }

    [Fact]
    public async Task ExecuteSqlCommand_NamedParametersReachTheProvider()
    {
        var affected = await Repo.ExecuteSqlCommand("UPDATE Products SET Price = @price WHERE Id = @id",
            new Dictionary<string, object> { ["@price"] = 777m, ["@id"] = 1 }, Ct);

        Assert.Equal(1, affected);
        await using var fresh = Db.CreateContext();
        Assert.Equal(777m, (await fresh.Products.FindAsync(new object[] { 1 }, Ct))!.Price);
    }

    [Fact]
    public async Task ExecuteSqlCommand_NullParameterValue_BecomesDbNull_NoParameters_Works_BlankThrows()
    {
        Assert.Equal(1, await Repo.ExecuteSqlCommand("UPDATE Products SET Description = @d WHERE Id = 2", new Dictionary<string, object> { ["@d"] = null! }, Ct));
        Assert.Equal(25, await Repo.ExecuteSqlCommand("UPDATE Products SET Tenant = Tenant", null, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Repo.ExecuteSqlCommand(" ", null, Ct));
    }

    [Fact]
    public void SqlQuery_RawQuery_ReturnsEntities_QueryableOverloadIsNotSupported()
    {
        var items = Repo.SqlQuery("SELECT * FROM Products WHERE Id < {0}", CommandType.Text, 4).ToList();

        Assert.Equal(3, items.Count);
        Assert.Throws<NotSupportedException>(() => Repo.SqlQuery(Context.Products, "SELECT 1", CommandType.Text));
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeTheContext()
    {
        Repo.Dispose();

        Assert.Equal(25, await Context.Products.CountAsync(Ct)); // would throw ObjectDisposedException before (N-9)
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GenericReadRepository<Product, int>(null!));
    }
}
