using System.Linq.Expressions;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services.ViewModels;

namespace TabTabGo.Core.Services;

public interface IBaseReadService<TEntity, TKey> where TEntity : class, IEntity
{
    #region Get Functions
    #region Get Functions
    Task<IEnumerable<TEntity>> Get(Expression<Func<TEntity, bool>> query, CancellationToken cancellationToken = default(CancellationToken));
    /// <summary>
    /// 
    /// </summary>
    /// <param name="oDataQueryOptions">ODataQueryOptions object</param>
    /// <param name="fixCriteria"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PageList<object>> Get(object oDataQueryOptions, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default(CancellationToken));
    Task<TEntity> Get(TKey id, DateTimeOffset? lastUpdatedDate = null, string[] includeProperties = null, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default(CancellationToken));
    #endregion
    
    #region GetViewModel Functions
    Task<PageList<object>> GetViewModels(object oDataQueryOptions, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default(CancellationToken));
    Task<PageList<TResult>> GetViewModels<TResult>(object oDataQueryOptions, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default(CancellationToken)) where TResult : class;
    Task<PageList<TResult>> GetCustomViewModels<TResult>(Func<TEntity, TResult> mapper, object oDataQueryOptions, Expression<Func<TEntity, bool>> fixCriteria = null, string[] includeProperties = null, CancellationToken cancellationToken = default(CancellationToken)) where TResult : class;
    Task<object> GetViewModel(TKey id, DateTimeOffset? lastUpdatedDate = null, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default(CancellationToken));
    Task<TResult> GetViewModel<TResult>(TKey id, DateTimeOffset? lastUpdatedDate = null, Expression<Func<TEntity, bool>> fixCriteria = null, CancellationToken cancellationToken = default(CancellationToken)) where TResult : class;
    Task<TResult> GetCustomViewModel<TResult>(Func<TEntity, TResult> mapper, TKey id, DateTimeOffset? lastUpdatedDate = null, Expression<Func<TEntity, bool>> fixCriteria = null, string[] includeProperties = null, CancellationToken cancellationToken = default(CancellationToken)) where TResult : class;
    #endregion

    #endregion

    Task<bool> Exists(TKey id, CancellationToken cancellationToken = default(CancellationToken));
    object MapToViewModel(TEntity entity);
    TResult MapToViewModel<TResult>(TEntity entity) where TResult : class;
    Task<Stream> ExportFile<TResult>(ExportConfiguration config,
        object oDataQueryOptions,
        Expression<Func<TEntity, bool>> fixCriteria = null,
        string[] includeProperties = null,
        CancellationToken cancellationToken = default(CancellationToken)) where TResult : class;
    
}