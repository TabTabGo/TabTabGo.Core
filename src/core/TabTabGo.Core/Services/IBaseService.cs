using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;

namespace TabTabGo.Core.Services;

public interface IBaseService<TEntity, TKey> :IBaseReadService<TEntity, TKey> where TEntity : class, IEntity
{
  
    #region Update Functions
    Task<TEntity> Update(TKey id, TEntity entity, CancellationToken cancellationToken = default(CancellationToken));
    Task<TEntity> UpdateChanges(TKey id, TEntity entity, CancellationToken cancellationToken);
    Task<TEntity> Update(TKey id, JsonPatchDocument<TEntity> entity, CancellationToken cancellationToken = default(CancellationToken));
    Task<TEntity> Update(TKey id, JObject entity, CancellationToken cancellationToken = default(CancellationToken));
    void HandleJsonOperator<T>(Operation operation, object affectedObject);
    #endregion

    Task<TEntity> Create(TEntity entity, CancellationToken cancellationToken = default(CancellationToken));
    Task<TEntity> Delete(TKey id, CancellationToken cancellationToken = default(CancellationToken));
   
    Task<bool> CanDelete(TKey id, CancellationToken cancellationToken = default(CancellationToken));
    
   
    Task<bool> CanDelete(IEnumerable<TKey> ids, CancellationToken cancellationToken);
    Task<IEnumerable<TEntity>> Delete(IEnumerable<TKey> ids, CancellationToken cancellationToken);
}

