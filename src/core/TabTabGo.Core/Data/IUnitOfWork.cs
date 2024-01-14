using System.Data;
using System.Linq.Expressions;

namespace TabTabGo.Core.Data;

public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Get repository for entity within unit of work scope
    /// </summary>
    /// <param name="name"></param>
    /// <typeparam name="TEntity"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <returns></returns>
    IGenericRepository<TEntity, TKey> Repository<TEntity, TKey>(string? name = null) where TEntity : class;
    
    /// <summary>
    /// Begin transaction
    /// </summary>
    /// <param name="isolationLevel"></param>
    void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);
    
    /// <summary>
    /// commit transaction
    /// </summary>
    void Commit();
    
    /// <summary>
    /// revert transaction
    /// </summary>
    void Rollback();
    
    /// <summary>
    /// Save changes synchronously and return number of saved entities
    /// </summary>
    /// <returns>number of entities changes</returns>
    int SaveChanges();
    
    /// <summary>
    /// Save changes asynchronously and return number of saved entities
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Save changes in bulk synchronously and return number of saved entities
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<int> SaveChangesInBulkAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update entity context
    /// </summary>
    /// <param name="entity"></param>
    /// <typeparam name="TEntity"></typeparam>
    void AddOrUpdateGraph<TEntity>(TEntity entity) where TEntity : class;
    //void UpdateState<TEntity>(TEntity entity, EntityState state);
    
    /// <summary>
    /// Set entity state in db context to modified
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="propertyExpression"></param>
    /// <typeparam name="TEntity"></typeparam>
    /// <typeparam name="TProperty"></typeparam>
    void SetEntityStateModified<TEntity, TProperty>(TEntity entity, Expression<Func<TEntity, TProperty>> propertyExpression) where TEntity : class where TProperty : class;
    
    /// <summary>
    /// Remove navigation property from entity
    /// </summary>
    /// <param name="ownerEntity"></param>
    /// <param name="id"></param>
    /// <typeparam name="TEntity"></typeparam>
    /// <typeparam name="TOwnerEntity"></typeparam>
    void RemoveNavigationProperty<TEntity, TOwnerEntity>(TOwnerEntity ownerEntity, object id) where TEntity : class where TOwnerEntity : class;
    
    /// <summary>
    /// Get entity changes from db context
    /// </summary>
    /// <returns>list of changes</returns>
    dynamic? GetChanges();
}