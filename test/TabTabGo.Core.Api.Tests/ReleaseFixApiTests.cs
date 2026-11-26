using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TabTabGo.Core.Data;
using TabTabGo.Core.Services;
using TabTabGo.Core.TestKit;
using TabTabGo.Core.WebApi.Middlewares;

namespace TabTabGo.Core.Api.Tests;

/// <summary>Release review fixes DG-4 and DG-5.</summary>
public class ReleaseFixApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void FreshEntityService(IServiceCollection services) =>
        services.AddScoped<IBaseService<Product, int>>(sp =>
            new ReturningFreshEntityProductService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<ILogger<BaseService<Product, int>>>())
            { ScopeMembersPatchable = true });

    // --- DG-4: the response is what the service returns, not the object that was patched ------------------------------------

    [Theory]
    [InlineData("/api/products/3", "[{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"patched\"}]")]
    [InlineData("/api/products/json/3", "{\"name\":\"patched\"}")]
    public async Task Controller_Patch_AnswersTheEntityTheServiceReturned(string url, string body)
    {
        await using var app = await TestApp.StartAsync("controller", withMapper: false, services: FreshEntityService);

        var response = await app.Client.SendAsync(
            app.Request(HttpMethod.Patch, url, new StringContent(body, Encoding.UTF8, "application/json")), Ct);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("returned-by-service", json.GetProperty("name").GetString()); // the stale patched object used to be sent
    }

    // --- DG-5: constructors of the 8.x middleware stay -----------------------------------------------------------------------

    private static async Task<(HttpStatusCode Status, string Body)> Run(Func<RequestDelegate, ErrorHandlingMiddleware> create)
    {
        var middleware = create(_ => throw new InvalidOperationException("LEAKMARKER"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);

        context.Response.Body.Position = 0;
        return ((HttpStatusCode)context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(Ct));
    }

    [Fact]
    public async Task ErrorHandlingMiddleware_OneAndTwoArgumentConstructors_UseDefaultOptions()
    {
        var one = await Run(next => new ErrorHandlingMiddleware(next));
        var two = await Run(next => new ErrorHandlingMiddleware(next, NullLogger<ErrorHandlingMiddleware>.Instance));

        foreach (var (status, body) in new[] { one, two })
        {
            Assert.Equal(HttpStatusCode.InternalServerError, status);
            Assert.DoesNotContain("LEAKMARKER", body);
            Assert.DoesNotContain("details", body); // IncludeExceptionDetails is off by default
        }
    }

    [Fact]
    public async Task ErrorHandlingMiddleware_ResolvedFromDependencyInjection_UsesTheOptionsConstructor()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.Configure<ErrorHandlingMiddlewareOptions>(o => o.IncludeExceptionDetails = true);
        await using var app = builder.Build();
        ((Microsoft.AspNetCore.Builder.IApplicationBuilder)app).UseMiddleware<ErrorHandlingMiddleware>();
        ((Microsoft.AspNetCore.Builder.IApplicationBuilder)app).Run(_ => throw new InvalidOperationException("boom"));
        await app.StartAsync(Ct);

        var text = await (await app.GetTestClient().GetAsync("/", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("boom", JsonDocument.Parse(text).RootElement.GetProperty("details").GetString());
    }

    [Fact]
    public void ErrorHandlingMiddleware_ActivatorConstructorIsTheOptionsOne()
    {
        var marked = typeof(ErrorHandlingMiddleware).GetConstructors()
            .Where(c => c.GetCustomAttributes(typeof(ActivatorUtilitiesConstructorAttribute), false).Length > 0)
            .ToList();

        var single = Assert.Single(marked);
        Assert.Equal(3, single.GetParameters().Length);
    }

    [Fact]
    public async Task ErrorHandlingMiddleware_ApiExceptionWithDefaultMessage_DoesNotSendTheFrameworkDefaultWithTheTypeName()
    {
        var middleware = new ErrorHandlingMiddleware(_ => throw new TabTabGo.Core.Exceptions.ApiException());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(Ct);
        Assert.DoesNotContain("ApiException", body);
        Assert.Contains("An unexpected error occurred.", body);
    }
}
