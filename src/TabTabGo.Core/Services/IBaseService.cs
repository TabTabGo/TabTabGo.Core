using Microsoft.AspNetCore.JsonPatch;

namespace TabTabGo.Core.Services;

public interface IBaseService<TEntity, TKey> :IBaseReadService<TEntity, TKey> where TEntity : class, IEntity
{
  
    #region Update Functions
    Task<TEntity> Update(TKey id, TEntity entity, CancellationToken cancellationToken = default(CancellationToken));
    Task<TEntity> Update(TKey id, JsonPatchDocument<TEntity> entity, CancellationToken cancellationToken = default(CancellationToken));
    Task<TEntity> Update(TKey id, JObject entity, CancellationToken cancellationToken = default(CancellationToken));
    #endregion

    Task<TEntity> Create(TEntity entity, CancellationToken cancellationToken = default(CancellationToken));
    Task<TEntity> Delete(TKey id, CancellationToken cancellationToken = default(CancellationToken));
   
    Task<bool> CanDelete(TKey id, CancellationToken cancellationToken = default(CancellationToken));
    
   
    Task<bool> CanDelete(IEnumerable<TKey> ids, CancellationToken cancellationToken);
    Task<IEnumerable<TEntity>> Delete(IEnumerable<TKey> ids, CancellationToken cancellationToken);
}

