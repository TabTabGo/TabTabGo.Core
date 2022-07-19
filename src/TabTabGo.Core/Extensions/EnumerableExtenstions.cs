namespace TabTabGo.Core.Extensions;

public static class EnumerableExtenstions
{
    public static void Foreach<T>(this IEnumerable<T> collection, Action<T> action)
    {
        foreach (var item in collection)
        {
            action(item);
        }
    }

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

