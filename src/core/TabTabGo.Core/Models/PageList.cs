namespace TabTabGo.Core.Models;

/// <summary>
/// One page of results. <see cref="PageNumber"/> is 1-based (ADR 0004).
/// </summary>
/// <typeparam name="TResult">Item type</typeparam>
public class PageList<TResult> where TResult : class
{
    /// <summary>1-based page number.</summary>
    public int PageNumber { get; }

    /// <summary>Total number of items across all pages.</summary>
    public int TotalItems { get; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize is > 0 ? (int)Math.Ceiling((decimal)TotalItems / PageSize.Value) : 1;

    /// <summary>Page size.</summary>
    public int? PageSize { get; }

    /// <summary>Number of items on this page.</summary>
    public int Count => Items.Count;

    /// <summary>Items on this page.</summary>
    public IList<TResult> Items { get; }

    /// <summary>
    /// Gets the has previous page.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Gets the has next page.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>Creates a page.</summary>
    public PageList(IList<TResult> items, int totalItems, int? pageSize, int pageNumber = 1)
    {
        Items = items ?? new List<TResult>();
        TotalItems = totalItems;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    /// <summary>Creates a page from a query (1-based page number).</summary>
    public static PageList<TResult> Create(IQueryable<TResult> source, int pageNumber, int pageSize)
    {
        var count = source.Count();
        var items = source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new PageList<TResult>(items, count, pageSize, pageNumber);
    }

    /// <summary>Creates a mapped page from a query (1-based page number).</summary>
    public static PageList<TMappedResult> Create<TMappedResult>(IQueryable<TResult> source, int pageNumber, int pageSize, Func<TResult, TMappedResult> mapper)
        where TMappedResult : class
    {
        var count = source.Count();
        var items = source.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(x => mapper(x)).ToList();
        return new PageList<TMappedResult>(items, count, pageSize, pageNumber);
    }
}
