using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TabTabGo.Core.Api.Controllers;
using TabTabGo.Core.Data;
using TabTabGo.Core.Services;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Core.Api.Tests;

public class EntityControllerBaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StringContent Json(string json, string contentType = "application/json") => new(json, Encoding.UTF8, contentType);

    private static StringContent Patch(string json) => Json(json, "application/json");

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    private static Task<TestApp> Start(bool withMapper = true, bool withValidator = true, Action<IServiceCollection>? services = null) =>
        TestApp.StartAsync("controller", withMapper: withMapper, withValidator: withValidator, services: services);

    // --- search / get --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Search_ReturnsMappedPage()
    {
        await using var app = await Start();

        var body = await Body(await app.Client.SendAsync(app.Request(HttpMethod.Get, "/api/products?$top=3&$orderby=Id"), Ct));

        Assert.Equal(25, body.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Search_ScopeFromOverride_IsApplied()
    {
        await using var app = await Start();

        var body = await Body(await app.Client.SendAsync(app.Request(HttpMethod.Get, "/api/products", tenant: "A"), Ct));

        Assert.Equal(12, body.GetProperty("totalItems").GetInt32()); // SearchFixCriteria was never applied before (D-34)
    }

    [Fact]
    public async Task Get_Existing_Returns200_Missing404WithEntityName()
    {
        await using var app = await Start();

        var ok = await app.Client.SendAsync(app.Request(HttpMethod.Get, "/api/products/2"), Ct);
        var missing = await app.Client.SendAsync(app.Request(HttpMethod.Get, "/api/products/999"), Ct);
        var body = await Body(missing);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("PRODUCT_NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_OutsideScope_Is404()
    {
        await using var app = await Start();

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Get, "/api/products/1", tenant: "A"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client.SendAsync(app.Request(HttpMethod.Get, "/api/products/2", tenant: "A"), Ct)).StatusCode);
    }

    // --- export ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Export_NotOverridden_Is501()
    {
        await using var app = await Start();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/products/export/Csv", Json("{}")), Ct);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task Export_Overridden_StreamsFileWithEntityNameAndOneDot()
    {
        await using var app = await Start(services: s => s.AddScoped<IBaseService<Product, int>>(sp => new ExportingService(sp.GetRequiredService<IUnitOfWork>())));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/products/export/Json", Json("{}"), tenant: "A"), Ct);

        var name = response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName;
        Assert.StartsWith("Product_", name); // "EntityName_" literal before (D-9)
        Assert.EndsWith(".json", name);
        Assert.DoesNotContain("..", name);
    }

    // --- create ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_Valid_Is201_LocationIsTheResourceUrl()
    {
        await using var app = await Start();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/products", Json("{\"name\":\"fresh\",\"price\":5}")), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/products/{body.GetProperty("id").GetInt32()}", response.Headers.Location!.OriginalString); // "Product" before (D-11)
    }

    [Fact]
    public async Task Create_InvalidByValidator_Is400_SaysCreateNotUpdate()
    {
        await using var app = await Start();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/products", Json("{\"name\":\"invalid\"}")), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid create request", body.GetProperty("message").GetString());
        Assert.Equal("PRODUCT_INVALID_CREATE_REQUEST", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Create_ApiException_UsesItsStatus()
    {
        await using var app = await Start(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>()) { CreateRule = _ => "dup" }));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/products", Json("{\"name\":\"x\"}")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2001, (await Body(response)).GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Create_ServiceThrows_500BodyWithoutException()
    {
        await using var app = await Start(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>()) { CreateRule = _ => throw new InvalidOperationException("secret-internal-detail") }));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/products", Json("{\"name\":\"x\"}")), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret-internal-detail", text);
        Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("CREATE_PRODUCT_FAILED", JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void Constructor_NoMapperAndRequestIsNotAnEntity_FailsWithClearMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EntityControllerBase<Product, int, ProductViewModel, ProductCreateRequest, ProductUpdateRequest>(
                NullLogger.Instance, new TestProductService(new MinimalUnitOfWorkStub(), NullLogger<BaseService<Product, int>>.Instance), null, null));

        Assert.Contains("mapper", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentNullException>(() =>
            new EntityControllerBase<Product, int, Product, Product, Product>(null!, null!, null, null));
    }

    // --- patch ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Patch_ValidChange_200()
    {
        await using var app = await Start();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/3",
            Patch("[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"Renamed\"}]")), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed", (await Body(response)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Patch_InvalidChange_400()
    {
        await using var app = await Start();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/3",
            Patch("[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"invalid\"}]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var fresh = app.Db.CreateContext();
        Assert.Equal("Product 03", fresh.Products.Find(3)!.Name);
    }

    [Fact]
    public async Task Patch_MissingAndDefaultKey()
    {
        await using var app = await Start();

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/999", Patch("[]")), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/0", Patch("[]")), Ct)).StatusCode);
    }

    [Fact]
    public async Task Patch_OutsideScope_Is404AndUnchanged()
    {
        await using var app = await Start();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/1",
            Patch("[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"hijacked\"}]"), tenant: "A"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var fresh = app.Db.CreateContext();
        Assert.Equal("Product 01", fresh.Products.Find(1)!.Name);
    }

    [Fact]
    public async Task PatchJson_MergesOnlyProvidedProperties()
    {
        await using var app = await Start();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/json/5", Json("{\"description\":\"merged\"}")), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("merged", body.GetProperty("description").GetString());
        Assert.Equal("Product 05", body.GetProperty("name").GetString()); // not reset (D-13)
    }

    [Fact]
    public async Task PatchJson_NoValidatorRegistered_DoesNotThrowNullReference()
    {
        // _validateService was dereferenced without a null check (D-10)
        await using var app = await Start(withValidator: false);

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/json/5", Json("{\"description\":\"x\"}")), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PatchJson_InvalidMissingOutOfScope()
    {
        await using var app = await Start();

        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/json/5", Json("{\"name\":\"invalid\"}")), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/json/999", Json("{}")), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/json/1", Json("{\"description\":\"x\"}"), tenant: "A"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/api/products/json/0", Json("{}")), Ct)).StatusCode);
    }

    // --- delete --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_Existing_Ok_Missing404_OutsideScope404()
    {
        await using var app = await Start();

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/api/products/999"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/api/products/1", tenant: "A"), Ct)).StatusCode);
        await using (var untouched = app.Db.CreateContext())
        {
            Assert.True(untouched.Products.Find(1)!.IsEnabled);
        }

        var ok = await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/api/products/2", tenant: "A"), Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await using var fresh = app.Db.CreateContext();
        Assert.False(fresh.Products.Find(2)!.IsEnabled);
    }

    [Fact]
    public async Task Delete_NotAllowed_Is412()
    {
        await using var app = await Start(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>()) { DeleteRule = _ => "in use" }));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/api/products/3"), Ct);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Delete_UnexpectedFailure_Is500Sanitised()
    {
        await using var app = await Start(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>()) { DeleteRule = _ => throw new InvalidOperationException("secret-internal-detail") }));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/api/products/3"), Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret-internal-detail", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task DeleteMany_DisablesAll_AndIsScopeChecked()
    {
        await using var app = await Start();

        var denied = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/products/delete", Json("[2,3]"), tenant: "A"), Ct); // 3 is tenant B
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await using (var untouched = app.Db.CreateContext())
        {
            Assert.Equal(25, untouched.Products.Count(p => p.IsEnabled));
        }

        var ok = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/products/delete", Json("[2,4]"), tenant: "A"), Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await using var fresh = app.Db.CreateContext();
        Assert.Equal(2, fresh.Products.Count(p => !p.IsEnabled));
    }

    [Fact]
    public async Task DeleteMany_NotAllowed_Is412_Failure_Is500()
    {
        await using var blocked = await Start(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>()) { DeleteRule = p => p.Id == 3 ? "no" : null }));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await blocked.Client.SendAsync(blocked.Request(HttpMethod.Post, "/api/products/delete", Json("[2,3]")), Ct)).StatusCode);

        await using var failing = await Start(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>()) { DeleteRule = _ => throw new InvalidOperationException("secret-internal-detail") }));
        var response = await failing.Client.SendAsync(failing.Request(HttpMethod.Post, "/api/products/delete", Json("[2]")), Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret-internal-detail", await response.Content.ReadAsStringAsync(Ct));
    }

    private sealed class MinimalUnitOfWorkStub : IUnitOfWork
    {
        private readonly MinimalUnitOfWork _inner = new();

        public IGenericRepository<TEntity, TKey> Repository<TEntity, TKey>(string? name = null) where TEntity : class =>
            (IGenericRepository<TEntity, TKey>)(object)new StubRepository();

        public void BeginTransaction(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.ReadCommitted) => _inner.BeginTransaction(isolationLevel);

        public void Commit() => _inner.Commit();

        public void Rollback() => _inner.Rollback();

        public int SaveChanges() => 0;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> SaveChangesInBulkAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public void AddOrUpdateGraph<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public void SetEntityStateModified<TEntity, TProperty>(TEntity entity, System.Linq.Expressions.Expression<Func<TEntity, TProperty>> propertyExpression) where TEntity : class where TProperty : class
        {
        }

        public void RemoveNavigationProperty<TEntity, TOwnerEntity>(TOwnerEntity ownerEntity, object id) where TEntity : class where TOwnerEntity : class
        {
        }

        public IReadOnlyList<EntityChange> GetChanges() => Array.Empty<EntityChange>();

        public void Dispose()
        {
        }
    }

    private abstract class StubRepositoryBase
    {
    }

    private sealed class StubRepository : StubRepositoryBase, IGenericRepository<Product, int>
    {
        private static NotSupportedException Nope() => new();

        public Task<TabTabGo.Core.Models.PageList<TResult>> GetPageListAsync<TResult>(Func<Product, TResult> selector, System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, int pageNumber = 0, int pageSize = 20, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) where TResult : class => throw Nope();

        public IQueryable<TResult> GetQueryable<TResult>(System.Linq.Expressions.Expression<Func<Product, TResult>>? selector = null, System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null) where TResult : class => throw Nope();

        public IEnumerable<Product> Get(System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null) => throw Nope();

        public Task<IEnumerable<Product>> GetAsync(System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Nope();

        public IEnumerable<TResult> Get<TResult>(System.Linq.Expressions.Expression<Func<Product, TResult>> selector, System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null) where TResult : class => throw Nope();

        public Task<IEnumerable<TResult>> GetAsync<TResult>(System.Linq.Expressions.Expression<Func<Product, TResult>> selector, System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) where TResult : class => throw Nope();

        public Product? GetByKey(int key, string[]? includeProperties = null) => throw Nope();

        public Task<Product?> GetByKeyAsync(int key, string[]? includeProperties = null, CancellationToken cancellationToken = default) => throw Nope();

        public int Count(System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, string[]? includes = null, TabTabGo.Core.Enums.QueryFlags? flags = null) => throw Nope();

        public Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, string[]? includes = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Nope();

        public Product? FirstOrDefault(System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null) => throw Nope();

        public Task<Product?> FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Nope();

        public TResult? FirstOrDefault<TResult>(System.Linq.Expressions.Expression<Func<Product, TResult>> selector, System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null) where TResult : class => throw Nope();

        public Task<TResult?> FirstOrDefaultAsync<TResult>(System.Linq.Expressions.Expression<Func<Product, TResult>> selector, System.Linq.Expressions.Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) where TResult : class => throw Nope();

        public IQueryable<Product> GetQueryable(TabTabGo.Core.Enums.QueryFlags? flags = null) => throw Nope();

        public IQueryable<Product> GetQueryable(string[] includeProperties, TabTabGo.Core.Enums.QueryFlags? flags = null) => throw Nope();

        public Task<int> ExecuteSqlCommand(string query, IDictionary<string, object>? parameters = null, CancellationToken cancellationToken = default) => throw Nope();

        public IQueryable<Product> SqlQuery(string query, System.Data.CommandType cmdType, params object[] parameters) => throw Nope();

        public IQueryable<Product> SqlQuery(IQueryable<Product> queryable, string query, System.Data.CommandType cmdType, params object[] parameters) => throw Nope();

        public IQueryable<Product> IncludeProperties(IQueryable<Product> queryable, string[] includeProperties) => throw Nope();

        public IQueryable<Product> SetFlags(IQueryable<Product> queryable, TabTabGo.Core.Enums.QueryFlags? flags) => throw Nope();

        public Task<Product?> InsertAsync(Product entity, CancellationToken cancellationToken = default) => throw Nope();

        public Product? Insert(Product entity) => throw Nope();

        public void Insert(IEnumerable<Product> items) => throw Nope();

        public Task InsertAsync(IEnumerable<Product> items, CancellationToken cancellationToken = default) => throw Nope();

        public Product? Delete(int key) => throw Nope();

        public void Delete(System.Linq.Expressions.Expression<Func<Product, bool>> filter) => throw Nope();

        public Product? Delete(Product? entityToDelete) => throw Nope();

        public void Delete(IQueryable<Product> entitiesToDelete) => throw Nope();

        public void Update(System.Text.Json.Nodes.JsonObject entityToUpdate, System.Linq.Expressions.Expression<Func<Product, bool>> filter) => throw Nope();

        public Product? Update(Product entityToUpdate) => throw Nope();

        public void Update(IEnumerable<Product> items) => throw Nope();

        public Task UpdateAsync(System.Text.Json.Nodes.JsonObject entityToUpdate, System.Linq.Expressions.Expression<Func<Product, bool>> filter, CancellationToken cancellationToken = default) => throw Nope();

        public Task<Product?> UpdateAsync(Product entityToUpdate, CancellationToken cancellationToken = default) => throw Nope();

        public Task UpdateAsync(IEnumerable<Product> items, CancellationToken cancellationToken = default) => throw Nope();

        public bool IsModified(Product entity, string property) => throw Nope();

        public void Dispose()
        {
        }
    }

    private sealed class ExportingService(IUnitOfWork unitOfWork)
        : BaseService<Product, int>(unitOfWork, NullLogger<BaseService<Product, int>>.Instance)
    {
        public override System.Linq.Expressions.Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        public override Task<Stream> ExportFile<TResult>(TabTabGo.Core.ViewModels.ExportConfiguration config, object oDataQueryOptions, System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, string[]? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("exported")));
    }
}
