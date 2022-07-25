using System.Linq.Expressions;
namespace TabTabGo.Core.Extensions;

/// <summary>
/// Cache property extension methods.
/// </summary>
/// <remarks>This Code part of https://www.jhipster.tech/</remarks>
/// <typeparam name="T">Any Class</typeparam>
public static class PropertyAccessorCache<T> where T : class
{
    private static readonly IDictionary<string, LambdaExpression?> _cache;

    static PropertyAccessorCache()
    {
        var storage = new Dictionary<string, LambdaExpression?>();

        var t = typeof(T);
        var parameter = Expression.Parameter(t, "p");
        foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyAccess = Expression.MakeMemberAccess(parameter, property);
            var lambdaExpression = Expression.Lambda(propertyAccess, parameter);
            storage[property.Name] = lambdaExpression;
        }

        _cache = storage;
    }

    public static LambdaExpression? Get(string propertyName)
    {
        LambdaExpression? result;
        return _cache.TryGetValue(propertyName, out result) ? result : null;
    }
}