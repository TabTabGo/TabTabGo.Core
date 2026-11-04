using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Api.Controllers;
using TabTabGo.Core.Services;
using TabTabGo.Core.TestKit;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Api.Tests;

public class MapEntityRoutesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StringContent Json(string json, string contentType = "application/json") => new(json, Encoding.UTF8, contentType);

    private static StringContent Patch(string json) => Json(json, "application/json-patch+json");

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    // --- startup ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MapEntityRoutes_ScopeValidationOn_Starts()
    {
        // the services used to be resolved from the root provider at startup, which fails with scope validation (D-5)
        await using var app = await TestApp.StartAsync(validateScopes: true);

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products?$top=1"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MapEntityRoutes_TwoRequests_DistinctScopes()
    {
        var contexts = new List<object>();
        await using var app = await TestApp.StartAsync(services: s => s.AddScoped<ContextRecorder>(sp =>
        {
            var recorder = new ContextRecorder(sp.GetRequiredService<TestDbContext>());
            lock (contexts)
            {
                contexts.Add(recorder.Context);
            }

            return recorder;
        }), configure: a => a.Use(async (http, next) =>
        {
            _ = http.RequestServices.GetRequiredService<ContextRecorder>();
            await next();
        }));

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => app.Client.SendAsync(app.Request(HttpMethod.Get, "/products/1"), Ct)));

        Assert.Equal(6, contexts.Distinct().Count());
    }

    private sealed record ContextRecorder(TestDbContext Context);

    [Fact]
    public async Task MapEntityRoutes_NoService_FailsFastAtMapping()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            app.MapEntityRoutes<Product, int, ProductViewModel, ProductCreateRequest, ProductUpdateRequest>("products"));

        Assert.Contains("IBaseService", ex.Message);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task MapEntityRoutes_NoMapperAndRequestIsNotAnEntity_FailsFastWithClearMessage()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<IBaseService<Product, int>>(_ => null!);
        var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            app.MapEntityRoutes<Product, int, ProductViewModel, ProductCreateRequest, ProductUpdateRequest>("products"));

        Assert.Contains("IMapper", ex.Message);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task MapEntityRoutes_EmptyName_Throws()
    {
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => app.MapEntityRoutes<Product, int, Product, Product, Product>(" "));
        await app.DisposeAsync();
    }

    [Fact]
    public async Task MapEntityRoutes_NoMapperEntityAsRequest_Works()
    {
        await using var app = await TestApp.StartAsync(withMapper: false, withValidator: false, entityAsRequest: true);
        // here the create request type is the view model type, so only reads are exercised without a mapper
        var response = await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products/2"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Product 02", (await Body(response)).GetProperty("name").GetString());
    }

    // --- auth ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/products")]
    [InlineData("GET", "/products/1")]
    [InlineData("POST", "/products")]
    [InlineData("PATCH", "/products/1")]
    [InlineData("DELETE", "/products/1")]
    [InlineData("POST", "/products/export/Csv")]
    public async Task EveryRoute_Unauthenticated_Is401(string method, string url)
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(new HttpMethod(method), url, user: null), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- search -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Search_ReturnsPagedViewModels()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products?$top=5&$orderby=Id"), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(25, body.GetProperty("totalItems").GetInt32());
        Assert.Equal(5, body.GetProperty("items").GetArrayLength());
        Assert.Equal(1, body.GetProperty("pageNumber").GetInt32());
        Assert.False(body.GetProperty("items")[0].TryGetProperty("tenant", out _)); // view model, not entity
    }

    [Fact]
    public async Task Search_Filter_Applies()
    {
        await using var app = await TestApp.StartAsync();

        var body = await Body(await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products?$filter=Price gt 200"), Ct));

        Assert.Equal(5, body.GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task Search_NoMapper_ReturnsEntities()
    {
        await using var app = await TestApp.StartAsync(withMapper: false, entityAsRequest: true);

        var body = await Body(await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products?$top=1"), Ct));

        Assert.True(body.GetProperty("items")[0].TryGetProperty("tenant", out _));
    }

    [Fact]
    public async Task Search_SearchFixCriteria_IsApplied()
    {
        // the criteria used to be ignored on search (D-6)
        await using var app = await TestApp.StartAsync(options: o => o.SearchFixCriteria = p => p.Tenant == "A");

        var body = await Body(await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products"), Ct));

        Assert.Equal(12, body.GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task MapEntityRoutes_Search_BindsODataOptions()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products?$select=Name&$top=2&$orderby=Id"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- get -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_Existing_ReturnsViewModel()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products/3"), Ct);
        var body = await Body(response);

        Assert.Equal(3, body.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task NotFound_MessageNamesEntity()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products/999"), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PRODUCT_NOT_FOUND", body.GetProperty("code").GetString()); // "TEntity_Not_Found" before (D-9)
        Assert.Contains("Product", body.GetProperty("message").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Get_Expand_LoadsNavigation()
    {
        await using var app = await TestApp.StartAsync(withMapper: false, entityAsRequest: true);

        var body = await Body(await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products/1?expand=Category"), Ct));

        Assert.Equal("Hardware", body.GetProperty("category").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_OutsideGetFixCriteria_Is404()
    {
        await using var app = await TestApp.StartAsync(options: o => o.GetFixCriteria = p => p.Tenant == "A");

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products/1"), Ct)).StatusCode); // tenant B
        Assert.Equal(HttpStatusCode.OK, (await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products/2"), Ct)).StatusCode);
    }

    // --- create --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_Valid_Is201WithLocationAndViewModel()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/products", Json("{\"name\":\"fresh\",\"price\":5}")), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/products/{body.GetProperty("id").GetInt32()}", response.Headers.Location!.OriginalString);
        Assert.Equal("fresh", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Create_InvalidByValidator_Is400WithErrors()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/products", Json("{\"name\":\"invalid\"}")), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Name is not valid", body.GetProperty("errors").GetProperty("Name")[0].GetString());
    }

    [Fact]
    public async Task Create_ServiceThrowsApiException_UsesItsStatusAndNumber()
    {
        await using var app = await TestApp.StartAsync(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<TabTabGo.Core.Data.IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>())
            { CreateRule = _ => "taken" }));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/products", Json("{\"name\":\"x\"}")), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2001, body.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Create_ServiceThrows_500BodyWithoutException()
    {
        // the exception object used to be put in the body, which made the error path itself throw (D-7)
        await using var app = await TestApp.StartAsync(services: s => s.AddScoped<IBaseService<Product, int>>(_ => new ThrowingService()));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/products", Json("{\"name\":\"x\"}")), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var body = JsonDocument.Parse(text).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("CREATE_PRODUCT_FAILED", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("exception", out _));
        Assert.DoesNotContain("secret-internal-detail", text);
        Assert.DoesNotContain("at ", text.Replace("at least", string.Empty));
    }

    // --- patch -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Patch_ValidChange_200()
    {
        // every valid patch used to be rejected when a validator was registered (D-4)
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/3",
            Patch("[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"Renamed\"}]")), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed", body.GetProperty("name").GetString());
        await using var fresh = app.Db.CreateContext();
        Assert.Equal("Renamed", fresh.Products.Find(3)!.Name);
    }

    [Fact]
    public async Task Patch_InvalidChange_400()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/3",
            Patch("[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"invalid\"}]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PATCH_REQUEST", (await Body(response)).GetProperty("code").GetString());
        await using var fresh = app.Db.CreateContext();
        Assert.Equal("Product 03", fresh.Products.Find(3)!.Name);
    }

    [Fact]
    public async Task Patch_NoMapper_ReturnsTheEntityNotNull()
    {
        await using var app = await TestApp.StartAsync(withMapper: false, entityAsRequest: true);

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/4",
            Patch("[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"E\"}]")), Ct);

        Assert.Equal("E", (await Body(response)).GetProperty("name").GetString()); // Ok(null) before (D-6)
    }

    [Fact]
    public async Task Patch_Missing_Is404()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/999", Patch("[]")), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_DefaultKey_IsInvalidIdForNumericKeys()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/0", Patch("[]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_ID", (await Body(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Patch_OutsideScope_Is404AndNothingChanges()
    {
        await using var app = await TestApp.StartAsync(options: o => o.GetFixCriteria = p => p.Tenant == "A");

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/1",
            Patch("[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"hijacked\"}]")), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var fresh = app.Db.CreateContext();
        Assert.Equal("Product 01", fresh.Products.Find(1)!.Name);
    }

    [Fact]
    public async Task Patch_UnknownPath_GoesToExtraProperties()
    {
        await using var app = await TestApp.StartAsync(withMapper: false, entityAsRequest: true);

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/5",
            Patch("[{\"op\":\"add\",\"path\":\"/color\",\"value\":\"red\"}]")), Ct);

        Assert.Equal("red", (await Body(response)).GetProperty("color").GetString());
    }

    // --- delete ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_Existing_SoftDeletes()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/products/6"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PRODUCT_DELETED", (await Body(response)).GetProperty("code").GetString());
        await using var fresh = app.Db.CreateContext();
        Assert.False(fresh.Products.Find(6)!.IsEnabled);
    }

    [Fact]
    public async Task Delete_Missing_Is404()
    {
        await using var app = await TestApp.StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/products/999"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_OutsideScope_Is404AndStaysEnabled()
    {
        await using var app = await TestApp.StartAsync(options: o => o.GetFixCriteria = p => p.Tenant == "A");

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/products/1"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var fresh = app.Db.CreateContext();
        Assert.True(fresh.Products.Find(1)!.IsEnabled);
    }

    [Fact]
    public async Task Delete_NotAllowed_Is412()
    {
        await using var app = await TestApp.StartAsync(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<TabTabGo.Core.Data.IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>())
            { DeleteRule = _ => "in use" }));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/products/7"), Ct);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("PRODUCT_CANNOT_DELETE", (await Body(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Delete_ServiceThrowsUnexpected_Is500Sanitised()
    {
        await using var app = await TestApp.StartAsync(services: s => s.AddScoped<IBaseService<Product, int>>(_ => new ThrowingService()));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Delete, "/products/7"), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret-internal-detail", text);
    }

    // --- export ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Export_NotOverridden_Is501()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/products/export/Csv", Json("{}")), Ct);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task Export_DefaultFileNameUsesEntity()
    {
        await using var app = await TestApp.StartAsync(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new ExportingService(sp.GetRequiredService<TabTabGo.Core.Data.IUnitOfWork>())));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/products/export/Excel", Json("{}")), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var name = response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName;
        Assert.StartsWith("Product_", name);
        Assert.EndsWith(".xlsx", name);
        Assert.DoesNotContain("..", name); // the extension already has its dot
        Assert.Equal("exported", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Export_UsesExportFixCriteriaAndConfiguredName()
    {
        ExportingService? service = null;
        await using var app = await TestApp.StartAsync(
            options: o => o.ExportFixCriteria = p => p.Tenant == "A",
            services: s => s.AddScoped<IBaseService<Product, int>>(sp => service = new ExportingService(sp.GetRequiredService<TabTabGo.Core.Data.IUnitOfWork>())));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/products/export/Json", Json("{\"fileName\":\"mine.json\"}")), Ct);

        Assert.Equal("mine.json", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        Assert.NotNull(service?.LastFixCriteria);
    }

    // --- route options -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PrefixAndVersion_AreAppliedToTheRoutes()
    {
        await using var app = await TestApp.StartAsync(options: o =>
        {
            o.ControllerPrefix = "api";
            o.Version = "v1";
        });

        Assert.Equal(HttpStatusCode.OK, (await app.Client.SendAsync(app.Request(HttpMethod.Get, "/api/v1/products/1"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.SendAsync(app.Request(HttpMethod.Get, "/products/1"), Ct)).StatusCode);

        var created = await app.Client.SendAsync(app.Request(HttpMethod.Post, "/api/v1/products", Json("{\"name\":\"x\"}")), Ct);
        Assert.StartsWith("/api/v1/products/", created.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("api", null, "api/")]
    [InlineData(null, "v2", "v2/")]
    [InlineData("api", "v2", "api/v2/")]
    [InlineData("/api/", "/v2/", "api/v2/")]
    public void GetRoutePrefix_ComposesPrefixAndVersion(string? prefix, string? version, string expected)
    {
        var options = new EntityRouterOptions<Product, int, ProductViewModel, ProductCreateRequest, ProductUpdateRequest>
        {
            ControllerPrefix = prefix,
            Version = version,
        };

        Assert.Equal(expected, options.GetRoutePrefix());
    }

    [Fact]
    public void RouterOptions_DefaultsAreNull()
    {
        var options = new EntityRouterOptions<Product, int, ProductViewModel, ProductCreateRequest, ProductUpdateRequest>();

        Assert.Null(options.SearchFixCriteria);
        Assert.Null(options.GetFixCriteria);
        Assert.Null(options.ExportFixCriteria);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Lookup_ThrowingApiException_GetsTheStandardBodyWithoutTheMiddleware(string method)
    {
        // the scoped lookup ran outside the try block, so an ApiException escaped as a raw 500
        await using var app = await TestApp.StartAsync(services: s => s.AddScoped<IBaseService<Product, int>>(sp =>
            new ForbiddenLookupService(sp.GetRequiredService<TabTabGo.Core.Data.IUnitOfWork>())));

        var response = await app.Client.SendAsync(app.Request(new HttpMethod(method), "/products/2",
            method == "PATCH" ? Patch("[]") : null), Ct);
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("NO_ACCESS", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    private sealed class ForbiddenLookupService(TabTabGo.Core.Data.IUnitOfWork unitOfWork)
        : BaseService<Product, int>(unitOfWork, Microsoft.Extensions.Logging.Abstractions.NullLogger<BaseService<Product, int>>.Instance)
    {
        public override System.Linq.Expressions.Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        public override Task<Product?> GetByKey(int id, DateTimeOffset? lastUpdatedDate = null, string[]? includeProperties = null,
            System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, CancellationToken cancellationToken = default) =>
            throw new TabTabGo.Core.Exceptions.ApiException("no", statusCode: HttpStatusCode.Forbidden, code: "NO_ACCESS");
    }

    private sealed class ThrowingService : IBaseService<Product, int>
    {
        private static InvalidOperationException Boom() => new("secret-internal-detail at Some.Internal.Class");

        public Task<Product> Update(int id, Product entity, CancellationToken cancellationToken = default) => throw Boom();

        public Task<Product> UpdateChanges(int id, Product entity, CancellationToken cancellationToken) => throw Boom();

        public Task<Product> Update(int id, Microsoft.AspNetCore.JsonPatch.JsonPatchDocument<Product> entity, CancellationToken cancellationToken = default) => throw Boom();

        public Task<Product> Update(int id, System.Text.Json.Nodes.JsonObject entity, CancellationToken cancellationToken = default) => throw Boom();

        public void HandleJsonOperator(Microsoft.AspNetCore.JsonPatch.Operations.Operation operation, object affectedObject) => throw Boom();

        public Task<Product> Create(Product? entity, CancellationToken cancellationToken = default) => throw Boom();

        public Task<Product> Delete(int id, CancellationToken cancellationToken = default) => throw Boom();

        public Task<bool> CanDelete(int id, CancellationToken cancellationToken = default) => throw Boom();

        public Task<bool> CanDelete(IEnumerable<int> ids, CancellationToken cancellationToken) => throw Boom();

        public Task<IEnumerable<Product>> Delete(IEnumerable<int> ids, CancellationToken cancellationToken) => throw Boom();

        public Task<IEnumerable<Product>> GetList(System.Linq.Expressions.Expression<Func<Product, bool>> query, string[]? includeProperties = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Boom();

        public Task<TabTabGo.Core.Models.PageList<Product>> GetPageList(System.Linq.Expressions.Expression<Func<Product, bool>> query, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, int page = 0, int pageSize = 25, string[]? includeProperties = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Boom();

        public Task<TabTabGo.Core.Models.PageList<Product>> GetPageList(object oDataQueryOptions, System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, string[]? includeProperties = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) => throw Boom();

        // reads answer normally so the write routes get past their lookups
        public Task<Product?> GetByKey(int id, DateTimeOffset? lastUpdatedDate = null, string[]? includeProperties = null, System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(new Product { Id = id });

        public Task<TabTabGo.Core.Models.PageList<object>> GetViewModels(object oDataQueryOptions, System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, CancellationToken cancellationToken = default) => throw Boom();

        public Task<TabTabGo.Core.Models.PageList<TResult>> GetViewModels<TResult>(object oDataQueryOptions, Func<Product, TResult> mapper, System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, string[]? includeProperties = null, CancellationToken cancellationToken = default) where TResult : class => throw Boom();

        public Task<TResult?> GetViewModelByKey<TResult>(int id, Func<Product, TResult> mapper, DateTimeOffset? lastUpdatedDate = null, string[]? includeProperties = null, System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, CancellationToken cancellationToken = default) where TResult : class => throw Boom();

        public Task<bool> Exists(int id, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<Stream> ExportFile<TResult>(ExportConfiguration config, object oDataQueryOptions, System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, string[]? includeProperties = null, CancellationToken cancellationToken = default) where TResult : class => throw new NotImplementedException();

        public System.Linq.Expressions.Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public int GetKey(Product entity) => entity.Id;
    }

    private sealed class ExportingService(TabTabGo.Core.Data.IUnitOfWork unitOfWork)
        : BaseService<Product, int>(unitOfWork, Microsoft.Extensions.Logging.Abstractions.NullLogger<BaseService<Product, int>>.Instance)
    {
        public System.Linq.Expressions.Expression<Func<Product, bool>>? LastFixCriteria { get; private set; }

        public override System.Linq.Expressions.Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        public override Task<Stream> ExportFile<TResult>(ExportConfiguration config, object oDataQueryOptions, System.Linq.Expressions.Expression<Func<Product, bool>>? fixCriteria = null, string[]? includeProperties = null, CancellationToken cancellationToken = default)
        {
            LastFixCriteria = fixCriteria;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("exported")));
        }
    }

    [Fact]
    public async Task Patch_ReplaceTenant_MovingOutOfScope_Is404AndNotPersisted()
    {
        // the scope was checked before the patch but never after it, so a tenant could be moved to another tenant (R2)
        await using var app = await TestApp.StartAsync(options: o => o.GetFixCriteria = p => p.Tenant == "A");

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2",
            Patch("[{\"op\":\"replace\",\"path\":\"/tenant\",\"value\":\"B\"}]")), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var fresh = app.Db.CreateContext();
        Assert.Equal("A", fresh.Products.Find(2)!.Tenant);
    }

    [Fact]
    public async Task Patch_ChangeWithinScope_StillWorks()
    {
        await using var app = await TestApp.StartAsync(options: o => o.GetFixCriteria = p => p.Tenant == "A");

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2",
            Patch("[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"fine\"}]")), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/id")]
    [InlineData("/Id")]
    [InlineData("/isEnabled")]
    [InlineData("/createdBy")]
    [InlineData("/updatedDate")]
    [InlineData("/category")]
    [InlineData("/category/name")]
    [InlineData("/lines")]
    public async Task Patch_ProtectedProperty_Is400AndNothingChanges(string path)
    {
        await using var app = await TestApp.StartAsync(withMapper: true);

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2",
            Patch("[{\"op\":\"replace\",\"path\":\"" + path + "\",\"value\":\"x\"}]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PROTECTED_PROPERTY", (await Body(response)).GetProperty("code").GetString());
        await using var fresh = app.Db.CreateContext();
        var product = fresh.Products.Find(2)!;
        Assert.True(product.IsEnabled);
        Assert.Equal("Product 02", product.Name);
    }

    [Fact]
    public async Task Patch_MoveFromProtectedProperty_Is400()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2",
            Patch("[{\"op\":\"move\",\"from\":\"/id\",\"path\":\"/name\"}]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_TooManyExtraProperties_Is400()
    {
        await using var app = await TestApp.StartAsync(withMapper: false, entityAsRequest: true);
        var operations = string.Join(",", Enumerable.Range(0, 101).Select(i => "{\"op\":\"add\",\"path\":\"/k" + i + "\",\"value\":1}"));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2", Patch("[" + operations + "]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("EXTRA_PROPERTIES_LIMIT", (await Body(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Patch_ManyExtraPropertyOperations_AreCappedAsAWhole_AndNothingIsPersisted()
    {
        // each operation was checked against an empty dictionary, so a thousand small ones all passed
        await using var app = await TestApp.StartAsync();
        var operations = string.Join(",", Enumerable.Range(0, 1000).Select(i => "{\"op\":\"add\",\"path\":\"/extraProperties/k" + i + "\",\"value\":1}"));

        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2", Patch("[" + operations + "]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("EXTRA_PROPERTIES_LIMIT", (await Body(response)).GetProperty("code").GetString());
        await using var fresh = app.Db.CreateContext();
        Assert.Empty(fresh.Products.Find(2)!.ExtraProperties);
    }

    [Fact]
    public async Task Patch_NewKeysOnTopOfAnExistingBag_AreCappedAgainstTheEntitiesRealBag()
    {
        await using var app = await TestApp.StartAsync();
        await using (var context = app.Db.CreateContext())
        {
            var product = context.Products.Find(2)!;
            for (var i = 0; i < 95; i++)
            {
                product.ExtraProperties["old" + i] = i;
            }

            await context.SaveChangesAsync(Ct);
        }

        var operations = string.Join(",", Enumerable.Range(0, 10).Select(i => "{\"op\":\"add\",\"path\":\"/extraProperties/new" + i + "\",\"value\":1}"));
        var response = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2", Patch("[" + operations + "]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var fresh = app.Db.CreateContext();
        Assert.Equal(95, fresh.Products.Find(2)!.ExtraProperties.Count);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("move")]
    public async Task Patch_CopyOrMoveIntoExtraProperties_IsSizeCheckedOrRejected(string op)
    {
        await using var app = await TestApp.StartAsync();
        await using (var context = app.Db.CreateContext())
        {
            context.Products.Find(2)!.Description = new string('x', 20_000);
            await context.SaveChangesAsync(Ct);
        }

        var big = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2",
            Patch("[{\"op\":\"" + op + "\",\"from\":\"/description\",\"path\":\"/extraProperties/big\"}]")), Ct);
        var whole = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2",
            Patch("[{\"op\":\"" + op + "\",\"from\":\"/name\",\"path\":\"/extraProperties\"}]")), Ct);
        var wholeObject = await app.Client.SendAsync(app.Request(HttpMethod.Patch, "/products/2",
            Patch("[{\"op\":\"add\",\"path\":\"/extraProperties\",\"value\":{\"a\":1}}]")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, whole.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wholeObject.StatusCode);
        await using var fresh = app.Db.CreateContext();
        var stored = fresh.Products.Find(2)!;
        Assert.Empty(stored.ExtraProperties);
        Assert.Equal(20_000, stored.Description!.Length);
    }
}
