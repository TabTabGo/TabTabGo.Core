namespace TabTabGo.Core.Extensions;

/// <summary>
/// Helpers for <see cref="IEnumerable{T}"/>.
/// </summary>
public static class EnumerableExtenstions
{
    /// <summary>Runs an action for every item.</summary>
    public static void Foreach<T>(this IEnumerable<T> collection, Action<T> action)
    {
        foreach (var item in collection)
        {
            action(item);
        }
    }

    /// <summary>Projects every item into a new (already evaluated) sequence.</summary>
    public static IEnumerable<TResult> Foreach<T, TResult>(this IEnumerable<T> collection, Func<T, TResult> func)
    {
        var resultList = new List<TResult>();
        foreach (var item in collection)
        {
            resultList.Add(func(item));
        }
        return resultList;
    }
}
