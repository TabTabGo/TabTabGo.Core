using System.Linq.Expressions;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services.ViewModels;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Services;

public interface IBaseReadService<TEntity, TKey> where TEntity : class, IEntity
{
    #region Get Functions
    #region Get Functions
    Task<IEnumerable<TEntity>> GetList(Expression<Func<TEntity?, bool>> query, DateTimeOffset? lastUpdatedDate = null, string[] includeProperties = null, CancellationToken cancellationToken = default);
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