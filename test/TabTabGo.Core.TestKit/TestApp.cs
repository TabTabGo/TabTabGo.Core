using Microsoft.Extensions.Hosting;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using TabTabGo.Core.Api.Controllers;
using TabTabGo.Core.Data;
using TabTabGo.Core.Services;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.TestKit;

/// <summary>Authenticates a request when it carries the <c>X-Test-User</c> header.</summary>
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user.ToString()) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>Reads <see cref="JsonPatchDocument{T}"/> bodies with Newtonsoft, as hosts that call AddNewtonsoftJson do.</summary>
public class JsonPatchInputFormatter : Microsoft.AspNetCore.Mvc.Formatters.TextInputFormatter
{
    public JsonPatchInputFormatter()
    {
        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("application/json-patch+json");
        SupportedEncodings.Add(Encoding.UTF8);
    }

    protected override bool CanReadType(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(JsonPatchDocument<>);

    public override async Task<Microsoft.AspNetCore.Mvc.Formatters.InputFormatterResult> ReadRequestBodyAsync(
        Microsoft.AspNetCore.Mvc.Formatters.InputFormatterContext context, Encoding encoding)
    {
        using var reader = new StreamReader(context.HttpContext.Request.Body, encoding);
        var text = await reader.ReadToEndAsync();
        return Microsoft.AspNetCore.Mvc.Formatters.InputFormatterResult.Success(Newtonsoft.Json.JsonConvert.DeserializeObject(text, context.ModelType));
    }
}

/// <summary>MVC controller over <see cref="EntityControllerBase{TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest}"/>; the scope comes from the <c>X-Tenant</c> header.</summary>
[ApiController]
[Route("api/products")]
public class TestProductsController(
    ILogger<TestProductsController> logger,
    IBaseService<Product, int> service,
    IMapper<Product, ProductViewModel>? mapper = null,
    IValidationService<Product, ProductCreateRequest>? validator = null)
    : EntityControllerBase<Product, int, ProductViewModel, ProductCreateRequest, ProductUpdateRequest>(logger, service, mapper, validator)
{
    private Expression<Func<Product, bool>>? Scope()
    {
        var category = Request.Headers["X-Category"].ToString();
        if (!string.IsNullOrEmpty(category))
        {
            // a scope over a navigation: evaluated in memory it needs the navigation loaded
            return p => p.Category!.Name == category;
        }

        var tenant = Request.Headers["X-Tenant"].ToString();
        return string.IsNullOrEmpty(tenant) ? null : p => p.Tenant == tenant;
    }

    protected override Expression<Func<Product, bool>>? SearchFixCriteria => Scope();

    protected override Expression<Func<Product, bool>>? GetFixCriteria => Scope();

    protected override Expression<Func<Product, bool>>? ExportFixCriteria => Scope();

    [HttpGet]
    public Task<IActionResult> Search(ODataQueryOptions<Product> query, CancellationToken ct) => SearchEntities(query, ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(int id, DateTimeOffset? lastUpdatedDate, string? expand, CancellationToken ct) => GetEntity(id, lastUpdatedDate, expand, ct);

    [HttpPost("export/{format}")]
    public Task<IActionResult> Export(Enums.ExportType format, ODataQueryOptions<Product> query, [FromBody] ExportConfiguration? export, CancellationToken ct) =>
        ExportEntities(format, query, export, ct);

    [HttpPost]
    public Task<IActionResult> Create([FromBody] ProductCreateRequest request, CancellationToken ct) => CreateEntity(request, ct);

    [HttpPatch("{id}")]
    public Task<IActionResult> Patch(int id, [FromBody] JsonPatchDocument<Product> changes, CancellationToken ct) => PatchEntity(id, changes, ct);

    [HttpPatch("json/{id}")]
    public Task<IActionResult> PatchJson(int id, [FromBody] JsonObject changes, CancellationToken ct) => PatchEntityByJson(id, changes, ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) => DeleteEntity(id, ct);

    [HttpPost("delete")]
    public Task<IActionResult> DeleteMany([FromBody] IEnumerable<int> ids, CancellationToken ct) => DeleteEntities(ids, ct);
}

/// <summary>A running test host over a seeded SQLite database with the product routes (minimal API or MVC).</summary>
public sealed class TestApp : IAsyncDisposable
{
    private TestApp(WebApplication app, SqliteFixture db, FakeLogCollector logs)
    {
        App = app;
        Db = db;
        Logs = logs;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public SqliteFixture Db { get; }

    public FakeLogCollector Logs { get; }

    public HttpClient Client { get; }

    /// <param name="mode">"minimal" maps the generated routes, "controller" uses <see cref="TestProductsController"/>.</param>
    public static async Task<TestApp> StartAsync(
        string mode = "minimal",
        Action<EntityRouterOptions<Product, int, ProductViewModel, ProductCreateRequest, ProductUpdateRequest>>? options = null,
        bool withMapper = true,
        bool withValidator = true,
        bool validateScopes = true,
        Action<WebApplication>? configure = null,
        Action<IServiceCollection>? services = null,
        bool seed = true,
        bool entityAsRequest = false,
        Action<EntityRouterOptions<Product, int, Product, Product, Product>>? entityOptions = null)
    {
        var db = new SqliteFixture();
        if (seed)
        {
            db.Seed();
        }

        var logs = new FakeLogCollector();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Host.UseDefaultServiceProvider((_, o) =>
        {
            o.ValidateScopes = validateScopes;
            o.ValidateOnBuild = validateScopes;
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FakeLoggerProvider(logs));

        builder.Services.AddSingleton(db);
        builder.Services.AddScoped(_ => db.CreateContext());
        builder.Services.AddScoped<IUnitOfWork>(sp => new TestUnitOfWork(sp.GetRequiredService<TestDbContext>()));
        builder.Services.AddScoped<IBaseService<Product, int>>(sp =>
            new TestProductService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>()));
        if (withMapper)
        {
            builder.Services.AddScoped<IMapper<Product, ProductViewModel>, FakeProductMapper>();
        }

        if (withValidator)
        {
            builder.Services.AddScoped<IValidationService<Product, ProductCreateRequest>, FakeProductValidator>();
        }

        builder.Services.AddAuthentication(TestAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        services?.Invoke(builder.Services);

        if (mode == "controller")
        {
            builder.Services.AddControllers(o => o.InputFormatters.Insert(0, new JsonPatchInputFormatter()))
                .AddApplicationPart(typeof(TestProductsController).Assembly).AddOData(o => o.EnableQueryFeatures());
        }
        else
        {
            builder.Services.AddOData();
        }

        var app = builder.Build();
        configure?.Invoke(app);
        app.UseAuthentication();
        app.UseAuthorization();
        if (mode == "controller")
        {
            app.MapControllers();
        }
        else if (entityAsRequest)
        {
            // no mapper: the entity is the view model and the create request
            app.MapEntityRoutes<Product, int, Product, Product, Product>("products", entityOptions);
        }
        else
        {
            app.MapEntityRoutes<Product, int, ProductViewModel, ProductCreateRequest, ProductUpdateRequest>("products", options);
        }

        await app.StartAsync();
        return new TestApp(app, db, logs);
    }

    public HttpRequestMessage Request(HttpMethod method, string url, HttpContent? content = null, string? user = "tester", string? tenant = null, string? category = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (user != null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        if (tenant != null)
        {
            request.Headers.Add("X-Tenant", tenant);
        }

        if (category != null)
        {
            request.Headers.Add("X-Category", category);
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        Db.Dispose();
    }
}
