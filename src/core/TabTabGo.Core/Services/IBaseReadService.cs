using System.Linq.Expressions;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services.ViewModels;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Services;

public interface IBaseReadService<TEntity, TKey> where TEntity : class, IEntity
{
    #region Get Functions
    #region Get Functions

    /// <summary>
    /// Get List of Entities based on query
    /// </summary>
    /// <param name="query">filter query</param>

    /// <param name="lastUpdatedDate"></param>
    /// <param name="includeProperties"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IEnumerable<TEntity>> GetList(Expression<Func<TEntity?, bool>> query, string[]? includeProperties = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get Page list of Entities based on query
    /// </summary>
    /// <param name="query"></param>
    /// <param name="orderBy">Order by query</param>
    /// <param name="page">0 index page</param>
    /// <param name="pageSize">number of item to return</param>
    /// <param name="includeProperties"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PageList<TEntity>> GetPageList(Expression<Func<TEntity?, bool>> query,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        int page = 0, int pageSize = 25,
        string[]? includeProperties = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="oDataQueryOptions">ODataQueryOptions object</param>
    /// <param name="fixCriteria"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PageList<TEntity>> GetPageList(object oDataQueryOptions, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default);
    Task<TEntity?> GetByKey(TKey id, DateTimeOffset? lastUpdatedDate = null, string[] includeProperties = null, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default);
    #endregion
    
    #region GetViewModel Functions
    Task<PageList<object>> GetViewModels(object oDataQueryOptions, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default);
    Task<PageList<TResult>> GetViewModels<TResult>(object oDataQueryOptions, Func<TEntity, TResult> mapper, Expression<Func<TEntity, bool>> fixCriteria = null, string[] includeProperties = null, CancellationToken cancellationToken = default) where TResult : class;
    Task<TResult> GetViewModelByKey<TResult>(TKey id, Func<TEntity,TResult> mapper, DateTimeOffset? lastUpdatedDate = null, string[] includeProperties = null, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default) where TResult : class;
    #endregion

    #endregion

    Task<bool> Exists(TKey id, CancellationToken cancellationToken = default);
    Task<Stream> ExportFile<TResult>(ExportConfiguration config,
        object oDataQueryOptions,
        Expression<Func<TEntity, bool>> fixCriteria = null,
        string[] includeProperties = null,
        CancellationToken cancellationToken = default) where TResult : class;
    
}