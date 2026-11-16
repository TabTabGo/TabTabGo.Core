namespace TabTabGo.Core.Extensions;

/// <summary>
/// Helpers for <see cref="ICollection{T}"/>.
/// </summary>
public static class CollectionExtenstions
{
    /// <summary>Runs an action for every item.</summary>
    public static void Foreach<T>(this ICollection<T> collection, Action<T> action)
    {
        foreach (var item in collection)
        {
            action(item);
        }
    }

    /// <summary>Projects every item into a new collection.</summary>
    public static ICollection<TResult> Foreach<T, TResult>(this ICollection<T> collection, Func<T, TResult> func)
    {
        var resultList = new List<TResult>();
        foreach (var item in collection)
        {
            resultList.Add(func(item));
        }

        return resultList;
    }

    /// <summary>Adds every item to the collection.</summary>
    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
    {
        foreach (T obj in items)
            collection.Add(obj);
    }
}
