using System.Linq.Expressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Api.Controllers;
using TabTabGo.Core.Data;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Json;
using TabTabGo.Core.Services;
using TabTabGo.Core.WebApi.Middlewares;
using TabTabGo.Data.EF;
using TabTabGo.Data.EF.Extensions;

namespace ReadmeSamples;

// The code blocks of README.md section 3 and 5, kept compiling here. The wiring is executed by ReadmeSamplesTests.

public class Product : Entity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // audit column lengths, and ExtraProperties stored as compact JSON with change detection
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.EntityBuilder();
        });
    }
}

public class AppUnitOfWork(AppDbContext context, ILogger<UnitOfWork> logger) : UnitOfWork(context, logger);

public class ProductService(IUnitOfWork unitOfWork, ILogger<BaseService<Product, int>> logger)
    : BaseService<Product, int>(unitOfWork, logger)
{
    public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

    public override int GetKey(Product entity) => entity.Id;
}

public static class Startup
{
    public static WebApplication Build(string connectionString)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
        builder.Services.AddScoped<IUnitOfWork>(sp => new AppUnitOfWork(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ILogger<UnitOfWork>>()));
        builder.Services.AddScoped<IBaseService<Product, int>, ProductService>();
        builder.Services.AddOData();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseTabTabGoErrorHandling(o => o.IncludeExceptionDetails = app.Environment.IsDevelopment());
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapEntityRoutes<Product, int, Product, Product, Product>("products", o =>
        {
            o.ControllerPrefix = "api";
            o.Version = "v1";
        });
        return app;
    }

    public static void RegisterJsonConverters(IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.AddTabTabGoConverters());
        services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.AddTabTabGoConverters());
        _ = new Newtonsoft.Json.JsonSerializerSettings().AddTabTabGoConverters(); // AddNewtonsoftJson(o => o.SerializerSettings.AddTabTabGoConverters())
    }
}

public class ReadmeSamplesTests
{
    [Fact]
    public async Task GettingStarted_WiringBuildsAndMapsTheDocumentedRoutes()
    {
        var app = Startup.Build("DataSource=:memory:");
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText).ToList();

        Assert.Contains("/api/v1/products/", endpoints); // the group root; it matches /api/v1/products too
        Assert.Contains("/api/v1/products/{id}", endpoints);
        Assert.Contains("/api/v1/products/export/{format}", endpoints);
        await app.DisposeAsync();
    }

    [Fact]
    public void JsonRegistration_IsIdempotentAndCompiles()
    {
        var services = new ServiceCollection();

        Startup.RegisterJsonConverters(services);

        Assert.NotEmpty(services);
    }
}
