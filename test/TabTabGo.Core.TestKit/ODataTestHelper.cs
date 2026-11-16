using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.OData.UriParser;

namespace TabTabGo.Core.TestKit;

/// <summary>Builds <see cref="ODataQueryOptions{TEntity}"/> from a query string, without a web host.</summary>
public static class ODataTestHelper
{
    private static readonly IEdmModel Model = BuildModel();

    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EntitySet<Product>("Products");
        builder.EntitySet<Category>("Categories");
        return builder.GetEdmModel();
    }

    /// <summary>Parses the query string (for example <c>$filter=Price gt 10&amp;$top=5</c>).</summary>
    public static ODataQueryOptions<Product> Create(string queryString) => Create<Product>(queryString);

    public static ODataQueryOptions<T> Create<T>(string queryString) where T : class
    {
        var services = new ServiceCollection();
        services.AddOData();
        services.AddRouting();
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.QueryString = new QueryString("?" + queryString.TrimStart('?'));
        httpContext.Request.Path = "/Products";

        var entitySet = Model.EntityContainer.FindEntitySet(typeof(T) == typeof(Category) ? "Categories" : "Products");
        var feature = httpContext.ODataFeature();
        feature.Model = Model;
        feature.RoutePrefix = string.Empty;
        feature.Path = new ODataPath(new EntitySetSegment(entitySet));
        feature.Services = provider;

        var context = new ODataQueryContext(Model, typeof(T), feature.Path);
        return new ODataQueryOptions<T>(context, httpContext.Request);
    }
}
