using System.Linq.Expressions;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services.ViewModels;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Services;

/// <summary>
/// Base read service interface
/// </summary>
/// <typeparam name="TEntity"></typeparam>
/// <typeparam name="TKey"></typeparam>
public interface IBaseReadService<TEntity, TKey> where TEntity : class, IEntity
{
    #region Get Functions
    #region Get Functions

    /// <summary>
    /// Get List of Entities based on query
    /// </summary>
    /// <param name="query">filter query</param>
    /// <param name="includeProperties">properties to join </param>
    /// <param name="flags">query flags</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IEnumerable<TEntity>> GetList(Expression<Func<TEntity?, bool>> query, string[]? includeProperties = null, 
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Page list of Entities based on query
    /// </summary>
    /// <param name="query"></param>
    /// <param name="orderBy">Order by query</param>
    /// <param name="page">0 index page</param>
    /// <param name="pageSize">number of item to return</param>
    /// <param name="includeProperties"></param>
    /// <param name="flags"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PageList<TEntity>> GetPageList(Expression<Func<TEntity?, bool>> query,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        int page = 0, int pageSize = 25,
        string[]? includeProperties = null,
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="oDataQueryOptions">ODataQueryOptions object</param>
    /// <param name="fixCriteria"></param>
    /// <param name="includeProperties"></param>
    /// <param name="flags"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PageList<TEntity>> GetPageList(object oDataQueryOptions, 
        Expression<Func<TEntity, bool>>? fixCriteria = null, 
        string[]? includeProperties = null, 
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get entity bu key
    /// </summary>
    /// <param name="id"></param>
    /// <param name="lastUpdatedDate"></param>
    /// <param name="includeProperties"></param>
    /// <param name="fixCriteria"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<TEntity?> GetByKey(TKey id, DateTimeOffset? lastUpdatedDate = null,
        string[]? includeProperties = null,
        Expression<Func<TEntity, bool>>?fixCriteria = null,
        CancellationToken cancellationToken = default);
    #endregion
    
    #region GetViewModel Functions
    /// <summary>
    /// Get view model paging list using default mapper
    /// </summary>
    /// <param name="oDataQueryOptions"></param>
    /// <param name="fixCriteria"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PageList<object>> GetViewModels(object oDataQueryOptions,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get view model paging list
    /// </summary>
    /// <param name="oDataQueryOptions"></param>
    /// <param name="mapper"></param>
    /// <param name="fixCriteria"></param>
    /// <param name="includeProperties"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    Task<PageList<TResult>> GetViewModels<TResult>(object oDataQueryOptions,
        Func<TEntity, TResult> mapper,
        Expression<Func<TEntity, bool>>? fixCriteria = null
        , string[]? includeProperties = null,
        CancellationToken cancellationToken = default) where TResult : class;
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="mapper"></param>
    /// <param name="lastUpdatedDate"></param>
    /// <param name="includeProperties"></param>
    /// <param name="fixCriteria"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    Task<TResult?> GetViewModelByKey<TResult>(TKey id,
        Func<TEntity,TResult> mapper,
        DateTimeOffset? lastUpdatedDate = null,
        string[]? includeProperties = null,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default) where TResult : class;
    #endregion

    #endregion
    /// <summary>
    /// Check if entity key exists in repository 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<bool> Exists(TKey id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Export entity to a file based on odata query options
    /// </summary>
    /// <param name="config">export options</param>
    /// <param name="oDataQueryOptions">oData query option</param>
    /// <param name="fixCriteria">fix criteria</param>
    /// <param name="includeProperties"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    Task<Stream> ExportFile<TResult>(ExportConfiguration config,
        object oDataQueryOptions,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        string[]? includeProperties = null,
        CancellationToken cancellationToken = default) where TResult : class;
    
}