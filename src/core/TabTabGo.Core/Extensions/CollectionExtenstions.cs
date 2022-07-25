using System;
using System.Collections.Generic;


namespace TabTabGo.Core.Extensions
{
    public static class CollectionExtenstions
    {
        public static void Foreach<T>(this ICollection<T> collection, Action<T> action)
        {
            foreach (var item in collection)
            {
                action(item);
            }
        }

        public static ICollection<TResult> Foreach<T, TResult>(this ICollection<T> collection, Func<T, TResult> func)
        {

            var resultList = new List<TResult>();
            foreach (var item in collection)
            {
                resultList.Add(func(item));
            }
            return resultList;
        }
    }
}
