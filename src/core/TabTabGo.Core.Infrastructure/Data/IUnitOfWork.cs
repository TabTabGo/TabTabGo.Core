using System.Data;
using System.Linq.Expressions;

namespace TabTabGo.Core.Infrastructure.Data;

public interface IUnitOfWork : IDisposable
{
    IGenericRepository<TEntity, TKey> Repository<TEntity, TKey>(string name) where TEntity : class;
    void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);
    void Commit();
    void Rollback();
    int SaveChanges();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesInBulkAsync(CancellationToken cancellationToken = default);
    void AddOrUpdateGraph<TEntity>(TEntity entity) where TEntity : class;
    //void UpdateState<TEntity>(TEntity entity, EntityState state);
    void SetEntityStateModified<TEntity, TProperty>(TEntity entity, Expression<Func<TEntity, TProperty>> propertyExpression) where TEntity : class where TProperty : class;
    void RemoveNavigationProperty<TEntity, TOwnerEntity>(TOwnerEntity ownerEntity, object id) where TEntity : class where TOwnerEntity : class;
    object? GetChanges();
}