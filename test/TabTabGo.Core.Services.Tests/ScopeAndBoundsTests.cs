using System.Linq.Expressions;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TabTabGo.Core.Data;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.TestKit;
using TabTabGo.Core.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace TabTabGo.Core.Services.Tests;

public class ScopeAndBoundsTests : ServiceTestBase
{
    private static Expression<Func<Product, bool>> HardwareScope => p => p.Category!.Name == "Hardware";

    private async Task<int> AddOtherCategoryAsync()
    {
        await using var context = Db.CreateContext();
        var other = new Category { Name = "Other" };
        context.Categories.Add(other);
        await context.SaveChangesAsync(Ct);
        return other.Id;
    }

    // --- scope is checked in the database after the save (S3) -----------------------------------------------------------

    [Fact]
    public async Task UpdateChanges_ForeignKeyMovesTheEntityOutOfScope_IsRolledBackAndRejected()
    {
        var otherCategory = await AddOtherCategoryAsync();
        var product = (await Service.GetByKey(2, cancellationToken: Ct))!;
        product.CategoryId = otherCategory; // the navigation object still says Hardware: an in-memory check would pass
        product.Name = "moved";

        var ex = await Assert.ThrowsAsync<ApiException>(() => Service.UpdateChanges(2, product, HardwareScope, Ct));

        Assert.Equal(HttpStatusCode.NotFound, ex.HttpStatusCode);
        Assert.False(UnitOfWork.HasActiveTransaction);
        await using var fresh = Db.CreateContext();
        var stored = fresh.Products.Find(2)!;
        Assert.Equal("Product 02", stored.Name);
        Assert.NotEqual(otherCategory, stored.CategoryId);
        Assert.DoesNotContain("PostUpdate", Service.Calls); // no side effects of an update that was rolled back
    }

    [Fact]
    public async Task UpdateChanges_ChangeInsideTheScope_IsCommitted()
    {
        var product = (await Service.GetByKey(3, cancellationToken: Ct))!;
        product.Name = "inside";

        await Service.UpdateChanges(3, product, HardwareScope, Ct);

        await using var fresh = Db.CreateContext();
        Assert.Equal("inside", fresh.Products.Find(3)!.Name);
        Assert.False(UnitOfWork.HasActiveTransaction);
        Assert.Contains("PostUpdate", Service.Calls);
    }

    [Fact]
    public async Task UpdateChanges_NullScope_BehavesLikeThePlainOverload()
    {
        var product = (await Service.GetByKey(4, cancellationToken: Ct))!;
        product.Name = "plain";

        await Service.UpdateChanges(4, product, (Expression<Func<Product, bool>>?)null, Ct);

        await using var fresh = Db.CreateContext();
        Assert.Equal("plain", fresh.Products.Find(4)!.Name);
    }

    [Fact]
    public async Task UpdateChanges_InsideAnOuterTransaction_LeavesItToTheCaller()
    {
        var product = (await Service.GetByKey(5, cancellationToken: Ct))!;
        product.Name = "outer";
        UnitOfWork.BeginTransaction();

        await Service.UpdateChanges(5, product, HardwareScope, Ct);

        Assert.True(UnitOfWork.HasActiveTransaction);
        UnitOfWork.Rollback();
        await using var fresh = Db.CreateContext();
        Assert.Equal("Product 05", fresh.Products.Find(5)!.Name);
    }

    // --- bulk keys (R4) -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_MoreThanTheLimitOfKeys_Is400()
    {
        var ids = Enumerable.Range(1, BaseReadService<Product, int>.MaxBulkKeys + 1).ToList();

        var ex = await Assert.ThrowsAsync<ApiException>(() => Service.CanDelete(ids, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        Assert.Equal("TOO_MANY_IDS", ex.ErrorCode);
        await Assert.ThrowsAsync<ApiException>(() => Service.Delete(ids, Ct));
        await Assert.ThrowsAsync<ApiException>(() => Service.CountByKeys(ids, null, Ct));
    }

    [Fact]
    public async Task Delete_ExactlyTheLimitOfKeys_IsAccepted()
    {
        var ids = Enumerable.Range(1, BaseReadService<Product, int>.MaxBulkKeys).ToList();

        Assert.True(await Service.CanDelete(ids, Ct));
    }

    [Fact]
    public async Task BulkLookup_IsOneInList()
    {
        var capture = new CommandCapture();
        await using var context = Db.CreateContext(capture);
        var service = new TestProductService(new TestUnitOfWork(context), NullLogger<BaseService<Product, int>>.Instance);

        await service.CanDelete(new[] { 1, 2, 3, 4 }, Ct);

        var sql = Assert.Single(capture.Selects);
        Assert.Contains(" IN (", sql);
        Assert.DoesNotContain(" OR ", sql);
    }

    [Fact]
    public async Task CountByKeys_CountsOnlyKeysInsideTheScope_InOneQuery()
    {
        var capture = new CommandCapture();
        await using var context = Db.CreateContext(capture);
        var service = new TestProductService(new TestUnitOfWork(context), NullLogger<BaseService<Product, int>>.Instance);
        Expression<Func<Product, bool>> tenantA = p => p.Tenant == "A"; // even ids

        var inScope = await service.CountByKeys(new[] { 2, 4, 6, 6 }, tenantA, Ct);
        var mixed = await service.CountByKeys(new[] { 2, 3, 4 }, tenantA, Ct);

        Assert.Equal(3, inScope);
        Assert.Equal(2, mixed);
        Assert.Equal(2, capture.Selects.Count());
        Assert.Equal(0, await service.CountByKeys(Array.Empty<int>(), tenantA, Ct));
        Assert.Equal(3, await service.CountByKeys(new[] { 1, 2, 3 }, null, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CountByKeys(null!, null, Ct));
    }

    [Fact]
    public async Task CountByKeys_DefaultInterfaceMethod_LooksKeysUpOneByOne()
    {
        IBaseReadService<Product, int> port = new OnlyGetByKeyService();

        Assert.Equal(2, await port.CountByKeys(new[] { 1, 2, 2, 99 }, null, Ct));
    }

    // --- null key predicate (R7) ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task NullKeyPredicate_IsBuiltFromTheStoreMetadata_AndTheScopeIsStillApplied()
    {
        var service = new NullPredicateService(UnitOfWork);
        Expression<Func<Product, bool>> tenantA = p => p.Tenant == "A";

        Assert.Null(await service.GetByKey(1, fixCriteria: tenantA, cancellationToken: Ct)); // odd ids are tenant B: the scope held
        Assert.NotNull(await service.GetByKey(2, fixCriteria: tenantA, cancellationToken: Ct));
        Assert.True(await service.Exists(3, Ct));
        Assert.False(await service.Exists(999, Ct));
        var product = (await service.GetByKey(4, cancellationToken: Ct))!;
        product.Name = "no NRE";
        Assert.Equal("no NRE", (await service.UpdateChanges(4, product, Ct)).Name); // NullReferenceException before
    }

    [Fact]
    public async Task NullKeyPredicate_WithoutStoreMetadata_Throws()
    {
        var service = new NullPredicateReadService(new MinimalReadRepository());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetByKey(1, cancellationToken: Ct));

        Assert.Contains("GetKeyPredicate returned null", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Exists(1, Ct));
    }

    // --- OData reads: key order and tracking (Q2, R3) -------------------------------------------------------------------------

    [Fact]
    public void ODataQuery_WithoutOrderBy_OrdersByThePrimaryKey()
    {
        // Skip/Take without an order logged EF warning 10102
        var query = Service.GetQueryable(ODataTestHelper.Create("$top=100"), out _, out _, out _);

        Assert.Contains("ORDER BY \"p\".\"Id\"", query.ToQueryString());
    }

    [Fact]
    public void ODataQuery_WithOrderBy_KeepsTheCallersOrder()
    {
        var query = Service.GetQueryable(ODataTestHelper.Create("$orderby=Price desc"), out _, out _, out _);

        var sql = query.ToQueryString();
        Assert.Contains("DESC", sql);
        Assert.DoesNotContain("ORDER BY \"p\".\"Id\"", sql);
    }

    [Fact]
    public void ODataQuery_EnableTracking_TracksTheEntities()
    {
        _ = Service.GetQueryable(ODataTestHelper.Create(""), out _, out _, out _).ToList();
        Assert.Empty(Context.ChangeTracker.Entries()); // untracked by default

        _ = Service.GetQueryable(ODataTestHelper.Create(""), out _, out _, out _, flags: QueryFlags.EnableTracking).ToList();

        Assert.Equal(25, Context.ChangeTracker.Entries<Product>().Count());
    }

    // --- case-insensitive re-parse of $filter (N-24) -----------------------------------------------------------------------------

    [Fact]
    public void RewrittenFilter_ResolvesPropertyNamesCaseInsensitively()
    {
        var service = new Exposed(UnitOfWork);
        var options = ODataTestHelper.Create("$filter=price gt 200 and xmlExist(Description,'x')");

        var (filter, custom) = service.Populate(options);
        var query = (IQueryable<Product>)filter!.ApplyTo(Context.Products, new ODataQuerySettings());

        Assert.Single(custom!);
        Assert.Equal(5, query.Count()); // "price" was not found before the re-parse was made case-insensitive
    }

    [Fact]
    public void RewrittenOrderBy_ResolvesPropertyNamesCaseInsensitively()
    {
        var service = new Exposed(UnitOfWork);

        var orderBy = service.OrderBy(ODataTestHelper.Create("$orderby=price desc"));

        Assert.NotNull(orderBy);
        Assert.Equal("Product 25", orderBy.ApplyTo(Context.Products, new ODataQuerySettings()).Cast<Product>().First().Name);
    }

    [Fact]
    public void CustomQueryJoinQuery_DefaultIsEmpty()
    {
        var (alias, join) = new Exposed(UnitOfWork).DefaultJoin("Category");

        Assert.Equal(string.Empty, alias);
        Assert.Equal(string.Empty, join);
    }

    // --- patch name resolution: the CLR property decides (S2) ------------------------------------------------------------------

    [Theory]
    [InlineData("/tenantid")]
    [InlineData("/TENANTID")]
    [InlineData("/TenantId")]
    [InlineData("/tenant_id")]
    [InlineData("/TENANT_ID")]
    [InlineData("/tenantid/x")]
    public void EnsurePatchable_ProtectedClrProperty_IsRejectedWhateverTheSpelling(string path)
    {
        var patch = new JsonPatchDocument<Product>();
        patch.Operations.Add(new Operation<Product>("replace", path, null, 5));

        var ex = Assert.Throws<ApiException>(() => new StrictService(UnitOfWork).EnsurePatchable(patch));

        Assert.Equal("PROTECTED_PROPERTY", ex.ErrorCode);
        Assert.Equal(nameof(Product.TenantId), ex.Message.Split('\'')[1]);
    }

    [Fact]
    public void EnsurePatchable_MoveAndCopyFromProtectedProperty_IsRejected()
    {
        var patch = new JsonPatchDocument<Product>();
        patch.Operations.Add(new Operation<Product>("copy", "/name", "/tenant_id"));

        Assert.Throws<ApiException>(() => new StrictService(UnitOfWork).EnsurePatchable(patch));
    }

    [Theory]
    [InlineData("/tenantid~1x")] // "~1" is a '/' inside one segment: this is the extra property "tenantid/x"
    [InlineData("/ten~0ant")]
    [InlineData("/name")]
    [InlineData("/extraProperties/color")]
    public void EnsurePatchable_NamesThatAreNotProtected_AreAllowed(string path)
    {
        var patch = new JsonPatchDocument<Product>();
        patch.Operations.Add(new Operation<Product>("add", path, null, "x"));

        new StrictService(UnitOfWork).EnsurePatchable(patch);
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("TENANT_ID")]
    [InlineData("tenant_id")]
    public void EnsurePatchable_JsonObject_ResolvesTheNameBeforeChecking(string name)
    {
        Assert.Throws<ApiException>(() => new StrictService(UnitOfWork).EnsurePatchable(new JsonObject { [name] = 5 }));
        new TestProductService(UnitOfWork, NullLogger<BaseService<Product, int>>.Instance).EnsurePatchable(new JsonObject { [name] = 5 }); // not protected by default
    }

    [Fact]
    public void EnsurePatchable_ReplacingTheWholeExtraPropertiesBag_IsRejected_ButKeysAreAllowedWithinLimits()
    {
        var whole = new JsonPatchDocument<Product>();
        whole.Operations.Add(new Operation<Product>("replace", "/extraProperties", null, new Dictionary<string, object> { ["a"] = 1 }));
        var tooBig = new JsonPatchDocument<Product>();
        tooBig.Operations.Add(new Operation<Product>("add", "/extraProperties/big", null, new string('x', 20_000)));
        var fine = new JsonPatchDocument<Product>();
        fine.Operations.Add(new Operation<Product>("add", "/extraProperties/ok", null, "x"));
        var service = new TestProductService(UnitOfWork, NullLogger<BaseService<Product, int>>.Instance);

        Assert.Equal("PROTECTED_PROPERTY", Assert.Throws<ApiException>(() => service.EnsurePatchable(whole)).ErrorCode);
        Assert.Equal("EXTRA_PROPERTIES_LIMIT", Assert.Throws<ApiException>(() => service.EnsurePatchable(tooBig)).ErrorCode);
        service.EnsurePatchable(fine);
    }

    [Fact]
    public void ApplyJson_UsesTheServicesProtectionRule()
    {
        var strict = new Product { TenantId = 1, Name = "n" };
        var loose = new Product { TenantId = 1, Name = "n" };
        var changes = new JsonObject { ["tenant_id"] = 9, ["name"] = "changed" };

        new StrictService(UnitOfWork).ApplyJson(changes, strict);
        Service.ApplyJson(changes, loose);

        Assert.Equal(1, strict.TenantId); // the subclass protects it
        Assert.Equal("changed", strict.Name);
        Assert.Equal(9, loose.TenantId);
    }

    [Fact]
    public void ApplyJson_DefaultInterfaceMethod_UsesThePopulateDefaults()
    {
        IBaseService<Product, int> port = new OnlyGetByKeyService();
        var product = new Product { Name = "n", IsEnabled = true };

        port.ApplyJson(new JsonObject { ["name"] = "x", ["isEnabled"] = false }, product);
        port.EnsurePatchable(new JsonObject());
        port.EnsurePatchable(new JsonPatchDocument<Product>());

        Assert.Equal("x", product.Name);
        Assert.True(product.IsEnabled);
    }

    // --- BaseService hooks default to no-ops (Q3) ----------------------------------------------------------------------------------

    [Fact]
    public async Task BaseHooks_AreNoOps()
    {
        var service = new HookProbe(UnitOfWork);
        var product = new Product();

        await Task.WhenAll(
            service.CallPreCreate(product), service.CallPostCreate(product),
            service.PreUpdate(product, product), service.PreUpdate(product, new JsonObject()), service.PreUpdate(product, new JsonPatchDocument<Product>()),
            service.PostUpdate(product),
            service.CallPreDelete(product), service.CallPostDelete(product),
            service.CallPreDelete(new[] { product }), service.CallPostDelete(new[] { product }));

        Assert.True(service.IsValidToCreate(product, out var created));
        Assert.True(service.IsValidToUpdate(product, out var updated));
        Assert.True(service.IsValidToDelete(product, out var deleted));
        Assert.Equal(string.Empty, created + updated + deleted);
    }

    // --- localizer guard layers: one test each (Q1b) ---------------------------------------------------------------------------------

    [Fact]
    public void Localizer_Layer1_CultureShape_RejectsBeforeAnyLookup()
    {
        Assert.False(JsonStringLocalizer.IsValidCultureName("../x"));
        Assert.False(JsonStringLocalizer.IsValidCultureName("en/US"));
        Assert.False(JsonStringLocalizer.IsValidCultureName(new string('a', 36)));
        Assert.True(JsonStringLocalizer.IsValidCultureName("ar-AE"));
    }

    [Fact]
    public void Localizer_Layer2_FilePathCheck_KeepsThePathInsideTheResourcesFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ttg-loc2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var secretName = "ttg-secret-" + Guid.NewGuid().ToString("N");
        var secret = Path.Combine(Path.GetDirectoryName(root)!, secretName + ".json");
        File.WriteAllText(secret, "{\"Key\":\"leaked\"}");
        try
        {
            var localizer = new JsonStringLocalizer(new JsonStringLocalizerOptions { ResourcesPath = root, ResourceAssemblyName = string.Empty });

            Assert.Null(localizer.SafeFilePath("../" + secretName));
            Assert.Null(localizer.SafeFilePath("/etc/passwd"));
            Assert.Null(localizer.Parse("../" + secretName)); // the file exists next to the folder, and is still not read
            Assert.Equal(Path.Combine(root, "en.json"), localizer.SafeFilePath("en"));
        }
        finally
        {
            Directory.Delete(root, true);
            File.Delete(secret);
        }
    }

    [Fact]
    public void Localizer_Layer3_AllowList_OnlyKnowsResourcesThatExisted_WhenFirstListed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ttg-loc3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "en.json"), "{\"Hello\":\"Hello\"}");
        try
        {
            var localizer = new JsonStringLocalizer(new JsonStringLocalizerOptions { ResourcesPath = root, ResourceAssemblyName = string.Empty });
            Assert.Equal("Hello", localizer.GetString("Hello", "en")); // lists the folder once

            File.WriteAllText(Path.Combine(root, "zz.json"), "{\"Hello\":\"late\"}"); // well-shaped, exists, but was not in the list
            var before = JsonStringLocalizer.CacheCount;

            Assert.Equal("Hello", localizer.GetString("Hello", "zz"));
            Assert.Equal(before, JsonStringLocalizer.CacheCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Localizer_DistributedCacheConstructor_IsKept()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        var localizer = new JsonStringLocalizer((IDistributedCache)cache);

        Assert.NotNull(localizer);
        Assert.Equal("missing", localizer.GetString("missing", "en"));
    }

    // --- fakes ---------------------------------------------------------------------------------------------------------------------------

    private sealed class Exposed(IUnitOfWork unitOfWork) : BaseReadService<Product, int>(unitOfWork.Repository<Product, int>(), NullLogger<BaseReadService<Product, int>>.Instance)
    {
        public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        public (FilterQueryOption? Filter, List<CustomOdataFiler>? Custom) Populate(ODataQueryOptions<Product> options) => PopulateFilter(options);

        public OrderByQueryOption OrderBy(ODataQueryOptions<Product> options) => GetOdataOrderByQueryOption(options);

        public (string Alias, string JoinQuery) DefaultJoin(string navigation) => CustomQueryJoinQuery(navigation);
    }

    private sealed class NullPredicateService(IUnitOfWork unitOfWork)
        : BaseService<Product, int>(unitOfWork, NullLogger<BaseService<Product, int>>.Instance)
    {
        public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => null!;

        public override int GetKey(Product entity) => entity.Id;
    }

    private sealed class NullPredicateReadService(IGenericReadRepository<Product, int> repository)
        : BaseReadService<Product, int>(repository, NullLogger<BaseReadService<Product, int>>.Instance)
    {
        public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => null!;

        public override int GetKey(Product entity) => entity.Id;
    }

    private sealed class StrictService(IUnitOfWork unitOfWork)
        : BaseService<Product, int>(unitOfWork, NullLogger<BaseService<Product, int>>.Instance)
    {
        public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        protected override bool IsProtectedProperty(string propertyName) =>
            propertyName == nameof(Product.TenantId) || base.IsProtectedProperty(propertyName);
    }

    private sealed class HookProbe(IUnitOfWork unitOfWork) : BaseService<Product, int>(unitOfWork, NullLogger<BaseService<Product, int>>.Instance)
    {
        public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        public Task CallPreCreate(Product p) => PreCreate(p);

        public Task CallPostCreate(Product p) => PostCreate(p);

        public Task CallPreDelete(Product p) => PreDelete(p);

        public Task CallPostDelete(Product p) => PostDelete(p);

        public Task CallPreDelete(IEnumerable<Product> p) => PreDelete(p);

        public Task CallPostDelete(IEnumerable<Product> p) => PostDelete(p);
    }

    /// <summary>Implements only what the default interface methods need.</summary>
    private sealed class OnlyGetByKeyService : IBaseService<Product, int>
    {
        private static NotSupportedException Nope() => new();

        public Task<Product?> GetByKey(int id, DateTimeOffset? lastUpdatedDate = null, string[]? includeProperties = null, Expression<Func<Product, bool>>? fixCriteria = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(id < 10 ? new Product { Id = id } : null);

        public Task<Product> Update(int id, Product entity, CancellationToken cancellationToken = default) => throw Nope();

        public Task<Product> UpdateChanges(int id, Product entity, CancellationToken cancellationToken) => throw Nope();

        public Task<Product> Update(int id, JsonPatchDocument<Product> entity, CancellationToken cancellationToken = default) => throw Nope();

        public Task<Product> Update(int id, JsonObject entity, CancellationToken cancellationToken = default) => throw Nope();

        public void HandleJsonOperator(Operation operation, object affectedObject) => throw Nope();

        public Task<Product> Create(Product? entity, CancellationToken cancellationToken = default) => throw Nope();

        public Task<Product> Delete(int id, CancellationToken cancellationToken = default) => throw Nope();

        public Task<bool> CanDelete(int id, CancellationToken cancellationToken = default) => throw Nope();

        public Task<bool> CanDelete(IEnumerable<int> ids, CancellationToken cancellationToken) => throw Nope();

        public Task<IEnumerable<Product>> Delete(IEnumerable<int> ids, CancellationToken cancellationToken) => throw Nope();

        public Task<IEnumerable<Product>> GetList(Expression<Func<Product, bool>> query, string[]? includeProperties = null, QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Nope();

        public Task<TabTabGo.Core.Models.PageList<Product>> GetPageList(Expression<Func<Product, bool>> query, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, int page = 0, int pageSize = 25, string[]? includeProperties = null, QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Nope();

        public Task<TabTabGo.Core.Models.PageList<Product>> GetPageList(object oDataQueryOptions, Expression<Func<Product, bool>>? fixCriteria = null, string[]? includeProperties = null, QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Nope();

        public Task<TabTabGo.Core.Models.PageList<object>> GetViewModels(object oDataQueryOptions, Expression<Func<Product, bool>>? fixCriteria = null, CancellationToken cancellationToken = default) => throw Nope();

        public Task<TabTabGo.Core.Models.PageList<TResult>> GetViewModels<TResult>(object oDataQueryOptions, Func<Product, TResult> mapper, Expression<Func<Product, bool>>? fixCriteria = null, string[]? includeProperties = null, CancellationToken cancellationToken = default) where TResult : class => throw Nope();

        public Task<TResult?> GetViewModelByKey<TResult>(int id, Func<Product, TResult> mapper, DateTimeOffset? lastUpdatedDate = null, string[]? includeProperties = null, Expression<Func<Product, bool>>? fixCriteria = null, CancellationToken cancellationToken = default) where TResult : class => throw Nope();

        public Task<bool> Exists(int id, CancellationToken cancellationToken = default) => throw Nope();

        public Task<Stream> ExportFile<TResult>(ExportConfiguration config, object oDataQueryOptions, Expression<Func<Product, bool>>? fixCriteria = null, string[]? includeProperties = null, CancellationToken cancellationToken = default) where TResult : class => throw Nope();

        public Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public int GetKey(Product entity) => entity.Id;
    }

    [Fact]
    public void EnsureExtraPropertiesWithinLimits_ChecksTheWholeBag()
    {
        var fine = new Product();
        fine.ExtraProperties["a"] = 1;
        var tooMany = new Product();
        for (var i = 0; i < 101; i++)
        {
            tooMany.ExtraProperties["k" + i] = i;
        }

        var bigValue = new Product();
        bigValue.ExtraProperties["big"] = new string('x', 20_000);
        var longKey = new Product();
        longKey.ExtraProperties[new string('k', 101)] = 1;

        Service.EnsureExtraPropertiesWithinLimits(fine);
        Assert.Throws<ApiException>(() => Service.EnsureExtraPropertiesWithinLimits(tooMany));
        Assert.Throws<ApiException>(() => Service.EnsureExtraPropertiesWithinLimits(bigValue));
        Assert.Throws<ApiException>(() => Service.EnsureExtraPropertiesWithinLimits(longKey));
        Assert.Throws<ArgumentNullException>(() => Service.EnsureExtraPropertiesWithinLimits(null!));
        ((IBaseService<Product, int>)new OnlyGetByKeyService()).EnsureExtraPropertiesWithinLimits(tooMany); // the default interface method accepts
    }

    [Fact]
    public async Task Update_JsonPatch_ManyExtraPropertyOperations_AreRejectedBeforeTheSave()
    {
        var patch = new JsonPatchDocument<Product>();
        for (var i = 0; i < 300; i++)
        {
            patch.Operations.Add(new Operation<Product>("add", "/extraProperties/k" + i, null, 1));
        }

        var ex = await Assert.ThrowsAsync<ApiException>(() => Service.Update(6, patch, Ct));

        Assert.Equal("EXTRA_PROPERTIES_LIMIT", ex.ErrorCode);
        await using var fresh = Db.CreateContext();
        Assert.Empty(fresh.Products.Find(6)!.ExtraProperties);
    }
}
