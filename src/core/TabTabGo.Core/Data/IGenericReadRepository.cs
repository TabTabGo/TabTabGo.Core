using System.Data;
using System.Linq.Expressions;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Models;

namespace TabTabGo.Core.Data;

/// <summary>
/// Read side of the generic repository.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TKey">Entity key type</typeparam>
public interface IGenericReadRepository<TEntity, in TKey> : IDisposable where TEntity : class
{
    #region Get Methods

    /// <summary>
    /// Gets paging objects from database. <paramref name="pageNumber"/> is a 0-based page index; the returned
    /// <see cref="PageList{TResult}.PageNumber"/> is 1-based (ADR 0004). Without <paramref name="orderBy"/> the
    /// primary key order is used so pages are stable.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <param name="selector"></param>
    /// <param name="filter"></param>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    /// <param name="orderBy"></param>
    /// <param name="includeProperties"></param>
    /// <param name="flags"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PageList<TResult>> GetPageListAsync<TResult>(
        Func<TEntity, TResult> selector,
        Expression<Func<TEntity, bool>>? filter = null,
        int pageNumber = 0,
        int pageSize = 20, 
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        string[]? includeProperties = null,
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default) where TResult : class;

    /// <summary>
    /// Gets a page and projects it in the database (the selector is translated to SQL).
    /// The default implementation compiles the selector and calls <see cref="GetPageListAsync{TResult}"/>.
    /// </summary>
    /// <typeparam name="TResult">Projected type</typeparam>
    /// <param name="selector">Projection expression</param>
    /// <param name="filter">Filter</param>
    /// <param name="pageNumber">0-based page index</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="orderBy">Order by</param>
    /// <param name="includeProperties">Properties to include</param>
    /// <param name="flags">Query flags</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<PageList<TResult>> GetProjectedPageListAsync<TResult>(
        Expression<Func<TEntity, TResult>> selector,
        Expression<Func<TEntity, bool>>? filter = null,
        int pageNumber = 0,
        int pageSize = 20,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        string[]? includeProperties = null,
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default) where TResult : class
        => GetPageListAsync(selector.Compile(), filter, pageNumber, pageSize, orderBy, includeProperties, flags, cancellationToken);

    /// <summary>
    /// Gets the queryable.
    /// </summary>
    /// <param name="selector"></param>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToTake">The rows to take.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags"></param>
    /// <returns></returns>
    IQueryable<TResult> GetQueryable<TResult>(
        Expression<Func<TEntity, TResult>>? selector = null
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null) where TResult : class;

    /// <summary>
    /// Gets the specified filter.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToTake">The rows to take.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null);

    /// <summary>
    /// Gets the specified filter.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToTake">The rows to take.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IEnumerable<TEntity>> GetAsync(Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified filter.
    /// </summary>
    /// <param name="selector"></param>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToTake">The rows to take.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    IEnumerable<TResult> Get<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null) where TResult : class;

    /// <summary>
    /// Gets the specified filter.
    /// </summary>
    /// <param name="selector">The projection.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToTake">The rows to take.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IEnumerable<TResult>> GetAsync<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default) where TResult : class;
    
    /// <summary>
    /// Get Entity by entity key
    /// </summary>
    /// <param name="key"></param>
    /// <param name="includeProperties"></param>
    /// <returns></returns>
    TEntity? GetByKey(TKey key, string[]? includeProperties = null);
    /// <summary>
    /// Get Entity by entity key Async
    /// </summary>
    /// <param name="key"></param>
    /// <param name="includeProperties"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<TEntity?> GetByKeyAsync(TKey key, string[]? includeProperties = null, CancellationToken cancellationToken = default);
    #endregion

    #region Count Methods

    /// <summary>
    /// Counts the specified filter.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="includes"></param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    int Count(Expression<Func<TEntity, bool>>? filter = null
        , string[]? includes = null
        , QueryFlags? flags = null);

    /// <summary>
    /// Counts the specified filter.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="includes"></param>
    /// <param name="flags">list fo flags used for query</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? filter = null
        , string[]? includes = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether any entity matches the filter. The default implementation counts.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="flags">list of flags used for query</param>
    /// <param name="cancellationToken"></param>
    async Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? filter = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default)
        => await CountAsync(filter, null, flags, cancellationToken).ConfigureAwait(false) > 0;
    #endregion

    #region FirstOrDefault Methods
    /// <summary>
    /// First or default.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    TEntity? FirstOrDefault(Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
    );

    /// <summary>
    /// First or default.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>        
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default);

    /// <summary>
    /// First or default.
    /// </summary>
    /// <param name="selector"></param>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    TResult? FirstOrDefault<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null) where TResult : class;

    /// <summary>
    /// First or default.
    /// </summary>
    /// <param name="selector"></param>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<TResult?> FirstOrDefaultAsync<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default) where TResult : class;

    #endregion

    /// <summary>
    /// Gets a raw queryable to be used mainly in OData searches
    /// </summary>
    /// <param name="flags">flags do define query</param>
    /// <returns>Queryable of type TEntity</returns>
    IQueryable<TEntity> GetQueryable(QueryFlags? flags = null);

    /// <summary>
    /// Gets a raw queryable to be used mainly in OData searches
    /// </summary>
    /// <param name="includeProperties">a comma separated list of properties to include</param>
    /// <param name="flags">flags do define query</param>
    /// <returns>Queryable of type TModel</returns>
    IQueryable<TEntity> GetQueryable(string[] includeProperties, QueryFlags? flags = null);

    /// <summary>
    /// Executes a raw SQL command.
    /// </summary>
    /// <param name="query">SQL text with named parameters</param>
    /// <param name="parameters">Parameter values by name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of affected rows</returns>
    Task<int> ExecuteSqlCommand(string query, IDictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// SQLs the query.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cmdType"></param>
    /// <param name="parameters">The parameters.</param>
    /// <returns></returns>
    IQueryable<TEntity> SqlQuery(string query, CommandType cmdType, params object[] parameters);

    /// <summary>
    /// Get entities by run Sql query 
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="query"></param>
    /// <param name="cmdType"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    IQueryable<TEntity> SqlQuery(IQueryable<TEntity> queryable, string query, CommandType cmdType, params object[] parameters);
    
    /// <summary>
    /// Properties to be included in Query
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="includeProperties"></param>
    /// <returns></returns>
    IQueryable<TEntity> IncludeProperties(IQueryable<TEntity> queryable, string[] includeProperties);

    /// <summary>
    /// Set Flags to query 
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="flags"></param>
    /// <returns></returns>
    IQueryable<TEntity> SetFlags(IQueryable<TEntity> queryable, QueryFlags? flags);

    /// <summary>
    /// Counts the rows of a query asynchronously. The default implementation counts synchronously;
    /// EF implementations count in the database. Lets services without an EF reference stay async.
    /// </summary>
    /// <param name="query">The query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default)
        => Task.FromResult(query.Count());

    /// <summary>
    /// Materializes a query asynchronously. The default implementation runs synchronously;
    /// EF implementations run in the database.
    /// </summary>
    /// <param name="query">The query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default)
        => Task.FromResult(query.ToList());
}
