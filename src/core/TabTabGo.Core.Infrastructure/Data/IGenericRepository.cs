using System.Linq.Expressions;
using Newtonsoft.Json.Linq;
using TabTabGo.Core.Entities;


namespace TabTabGo.Core.Infrastructure.Data;

/// <summary>
/// 
/// </summary>
/// <typeparam name="TEntity">Entity</typeparam>
public interface IGenericRepository<TEntity, TKey> : IGenericReadRepository<TEntity, TKey>, IDisposable where TEntity : class
{
    #region Insert
    /// <summary>
    /// Inserts the specified entity async.
    /// </summary>Value
    /// <param name="entity">The entity.</param>
    Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default(CancellationToken));
    /// <summary>
    /// Inserts the specified entity.
    /// </summary>
    /// <param name="entity">The entity.</param>
    TEntity Insert(TEntity entity);

    /// <summary>
    /// Bulk add a huge collection of items. The method uses transaction scopes to ensure data integrity
    /// </summary>
    /// <param name="items">the collection of items to save</param>
    void Insert(IEnumerable<TEntity> items);
    /// <summary>
    /// Bulk add a huge collection of items async. The method uses transaction scopes to ensure data integrity
    /// </summary>
    /// <param name="items">the collection of items to save</param>
    Task InsertAsync(IEnumerable<TEntity> items, CancellationToken cancellationToken = default(CancellationToken));
    #endregion

    #region Delete
    /// <summary>
    /// Deletes the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    TEntity Delete(object? key);
    /// <summary>
    /// 
    /// </summary>
    /// <param name="filter">where clause to entities</param>        
    /// <returns>Number of success entity Deleted</returns>
    void Delete(Expression<Func<TEntity, bool>> filter);

    /// <summary>
    /// Deletes the specified entity.
    /// </summary>
    /// <param name="entityToDelete">The entity to delete.</param>
    TEntity Delete(TEntity? entityToDelete);

    // <summary>
    /// Deletes list of entities.
    /// </summary>
    /// <param name="entityToDelete">The entity to delete.</param>
    void Delete(IEnumerable<TEntity> entitiesToDelete);
    #endregion

    #region Update
    /// <summary>
    /// Update JObject
    /// </summary>
    /// <param name="entityToUpdate">Properties that need to be updated</param>
    /// <param name="filter">Where clause to update proprties for list of entities </param>
    /// <returns></returns>
    void Update(JObject entityToUpdate, Expression<Func<TEntity, bool>> filter);
    /// <summary>
    /// Uodate Entity
    /// </summary>
    /// <param name="entityToUpdate"></param>
    /// <returns></returns>
    TEntity Update(TEntity entityToUpdate);
    /// <summary>
    /// Update List of entities 
    /// </summary>
    /// <param name="items"></param>
    /// <returns></returns>
    void Update(IEnumerable<TEntity> items);
    /// <summary>
    /// Async Update JObject
    /// </summary>
    /// <param name="entityToUpdate"></param>
    /// <param name="filter"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task UpdateAsync(JObject entityToUpdate, Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default(CancellationToken));
    /// <summary>
    /// Update Entity Async
    /// </summary>
    /// <param name="entityToUpdate"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<TEntity> UpdateAsync(TEntity entityToUpdate, CancellationToken cancellationToken = default(CancellationToken));
    /// <summary>
    /// Update List of entities 
    /// </summary>
    /// <param name="items"></param>
    /// <returns></returns>
    Task UpdateAsync(IEnumerable<TEntity> items, CancellationToken cancellationToken = default(CancellationToken));
    #endregion

    /// <summary>
    /// check if a property is modified from its original value 
    /// </summary>
    /// <param name="entity">the entity to inspect</param>
    /// <param name="property">the name of the property to inspect</param>
    /// <returns>true if property has been modified from its original value</returns>
    bool IsModified(TEntity entity, string property);
}
