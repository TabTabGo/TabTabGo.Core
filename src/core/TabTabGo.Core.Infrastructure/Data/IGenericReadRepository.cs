using System.Data;
using System.Linq.Expressions;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Models;

namespace TabTabGo.Core.Infrastructure.Data;

public interface IGenericReadRepository<TEntity, TKey> : IDisposable where TEntity : class
{
    #region Get Methods

    /// <summary>
    /// Gets paging objects from database
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <param name="selector"></param>
    /// <param name="filter"></param>
    /// <param name="pageSize"></param>
    /// <param name="pageSize"></param>
    /// <param name="orderBy"></param>
    /// <param name="includeProperties"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PageList<TResult>> GetPageListAsync<TResult>(
        Func<TEntity, TResult> selector,
        Expression<Func<TEntity?, bool>> filter = null,
        int pageNumber = 0,
        int pageSize = 20, 
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
        string[] includeProperties = null,
        CancellationToken cancellationToken = default) where TResult : class;

    // <summary>
    /// Gets the queryable.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToTake">The rows to take.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags"></param>
    /// <returns></returns>
    IQueryable<TResult> GetQueryable<TResult>(
        Expression<Func<TEntity, TResult>> selector = null
        , Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
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
    IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
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
    /// <returns></returns>
    Task<IEnumerable<TEntity>> GetAsync(Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default(CancellationToken));

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
    IEnumerable<TResult> Get<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
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
    Task<IEnumerable<TResult>> GetAsync<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default(CancellationToken)) where TResult : class;
    /// <summary>
    /// Get Entitiy by entity key
    /// </summary>
    /// <param name="key"></param>
    /// <param name="includeProperties"></param>
    /// <returns></returns>
    TEntity? GetByKey(object? key, string[] includeProperties = null);
    /// <summary>
    /// Get Entitiy by entity key Async
    /// </summary>
    /// <param name="key"></param>
    /// <param name="includeProperties"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<TEntity?> GetByKeyAsync(object key, string[] includeProperties = null, CancellationToken cancellationToken = default(CancellationToken));
    #endregion

    #region Count Methods
    /// <summary>
    /// Counts the specified filter.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    int Count(Expression<Func<TEntity, bool>> filter = null
        , string[] includes = null
        , QueryFlags? flags = null);

    /// <summary>
    /// Counts the specified filter.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    Task<int> CountAsync(Expression<Func<TEntity, bool>> filter = null
        , string[] includes = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default(CancellationToken));
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
    TEntity FirstOrDefault(Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
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
    /// <returns></returns>
    Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// First or default.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    TResult FirstOrDefault<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null) where TResult : class;

    /// <summary>
    /// First or default.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>        
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags">list fo flags used for query</param>
    /// <returns></returns>
    Task<TResult> FirstOrDefaultAsync<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default(CancellationToken)) where TResult : class;

    #endregion

    /// <summary>
    /// Gets a raw queryable to be used mainly in OData searches
    /// </summary>
    /// <param name="flags">flags do define query</param>
    /// <returns>Queryable of type TEntity</returns>
    IQueryable<TEntity?> GetQueryable(QueryFlags? flags = null);

    /// <summary>
    /// Gets a raw queryable to be used mainly in OData searches
    /// </summary>
    /// <param name="includeProperties">a comma separated list of properties to include</param>
    /// <param name="flags">flags do define query</param>
    /// <returns>Queryable of type TModel</returns>
    IQueryable<TEntity?> GetQueryable(string[] includeProperties, QueryFlags? flags = null);

    Task<int> ExecuteSqlCommand(string query, IDictionary<string, object> parameters = null, CancellationToken cancellationToken = default(CancellationToken));
    /// <summary>
    /// SQLs the query.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cmdType"></param>
    /// <param name="parameters">The parameters.</param>
    /// <returns></returns>
    IQueryable<TEntity?> SqlQuery(string query, CommandType cmdType, params object[] parameters);
    /// <summary>
    /// Get entites by run Sql query 
    /// </summary>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    IQueryable<TEntity> SqlQuery(IQueryable<TEntity> queryable, string query, CommandType cmdType, params object[] parameters);
    /// <summary>
    /// Properties to be included in Query
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="includeProperties"></param>
    /// <returns></returns>
    IQueryable<TEntity?> IncludeProperties(IQueryable<TEntity?> queryable, string[] includeProperties);

    /// <summary>
    /// Set Flags to quary 
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="flags"></param>
    /// <returns></returns>
    IQueryable<TEntity?> SetFlags(IQueryable<TEntity?> queryable, QueryFlags? flags);    
}