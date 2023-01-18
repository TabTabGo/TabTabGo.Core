namespace TabTabGo.Core.Models
{ 

    public class PageList<TResult> where TResult : class
    {
        public int PageNumber { get; }
        public int TotalItems { get;  }
        public int TotalPages => PageSize is > 0 ? (int) Math.Ceiling((decimal)TotalItems / PageSize.Value) : 1;
        public int? PageSize { get;  }
        public int Count  => Items != null ? Items.Count : 0;

        public IList<TResult> Items { get; }

        /// <summary>
        /// Gets the has previous page.
        /// </summary>
        /// <value>The has previous page.</value>
        public bool HasPreviousPage => PageNumber > 1;

        /// <summary>
        /// Gets the has next page.
        /// </summary>
        /// <value>The has next page.</value>
        public bool HasNextPage => PageNumber < TotalPages;

        public PageList(IList<TResult> items, int totalItems , int? pageSize, int pageNumber =1)
        {
            Items = items;
            TotalItems = totalItems;
            PageNumber = pageNumber;
            PageSize = pageSize;
        }

    }
}