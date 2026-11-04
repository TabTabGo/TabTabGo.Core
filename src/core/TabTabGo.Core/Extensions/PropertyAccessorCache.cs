using System.Linq.Expressions;

namespace TabTabGo.Core.Extensions;

/// <summary>
/// Cache of property accessor expressions.
/// </summary>
/// <remarks>This Code part of https://www.jhipster.tech/</remarks>
/// <typeparam name="T">Any Class</typeparam>
public static class PropertyAccessorCache<T> where T : class
{
    private static readonly IDictionary<string, LambdaExpression?> Cache;

    static PropertyAccessorCache()
    {
        var storage = new Dictionary<string, LambdaExpression?>();

        var t = typeof(T);
        var parameter = Expression.Parameter(t, "p");
        foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            var propertyAccess = Expression.MakeMemberAccess(parameter, property);
            var lambdaExpression = Expression.Lambda(propertyAccess, parameter);
            storage[property.Name] = lambdaExpression;
        }

        Cache = storage;
    }

    /// <summary>Gets the <c>p =&gt; p.Property</c> expression, or null when the property does not exist.</summary>
    public static LambdaExpression? Get(string propertyName)
    {
        return Cache.TryGetValue(propertyName, out var result) ? result : null;
    }
}
