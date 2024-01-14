using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using TabTabGo.Core.Data;
using TabTabGo.Core.Extensions;

namespace TabTabGo.Data.EF.Repositories
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    /// <typeparam name="TKey"></typeparam>
    public class GenericRepository<TEntity, TKey> : GenericReadRepository<TEntity, TKey>, IDisposable,
        IGenericRepository<TEntity, TKey> where TEntity : class
    {
        public GenericRepository(DbContext dbContext) : base(dbContext)
        {
        }

        #region Insert

        public virtual void Insert(IEnumerable<TEntity> items)
        {
            _dbSet.AddRange(items);
        }

        public virtual Task InsertAsync(IEnumerable<TEntity> items,
            CancellationToken cancellationToken = default)
        {
            return _dbSet.AddRangeAsync(items, cancellationToken);
        }

        public virtual TEntity? Insert(TEntity entity)
        {
            return _dbSet.Add(entity).Entity;
        }

        public virtual async Task<TEntity?> InsertAsync(TEntity entity,
            CancellationToken cancellationToken = default)
        {
            var result = await _dbSet.AddAsync(entity, cancellationToken);
            return result.Entity;
        }

        #endregion

        #region Update

        public virtual void Update(IEnumerable<TEntity> items)
        {
            _dbSet.UpdateRange(items);
        }

        public virtual Task UpdateAsync(IEnumerable<TEntity> items,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => _dbSet.UpdateRange(items), cancellationToken);
        }

        public virtual void Update(JObject entityToUpdate, Expression<Func<TEntity, bool>> filter)
        {
            var entities = _dbSet.Where(filter!);
            foreach (var entity in entities)
            {
                if (entity != null) entityToUpdate.Populate<TEntity>(entity);
            }

            _dbSet.UpdateRange(entities);
        }

        public virtual TEntity? Update(TEntity entityToUpdate)
        {
            //var updatedInstance = _dbSet.Attach(entityToUpdate);
            //updatedInstance.State = Microsoft.EntityFrameworkCore.EntityState.Modified;
            var result = _dbSet.Update(entityToUpdate);
            return result.Entity;
        }

        public virtual Task UpdateAsync(JObject entityToUpdate, Expression<Func<TEntity, bool>> filter,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Update(entityToUpdate, filter), cancellationToken);
        }

        public virtual Task<TEntity?> UpdateAsync(TEntity entityToUpdate,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Update(entityToUpdate), cancellationToken);
        }

        #endregion

        #region Delete

        public virtual TEntity? Delete(object? key)
        {
            var entity = GetByKey(key);

            return Delete(entity);
        }

        public virtual void Delete(Expression<Func<TEntity, bool>> filter)
        {
            var entities = _dbSet.Where(filter!);
            Delete(entities);
        }

        public virtual TEntity? Delete(TEntity? entityToDelete)
        {
            var removedEntity = _dbSet.Remove(entityToDelete);
            return removedEntity.Entity;
        }

        public virtual void Delete(IQueryable<TEntity?> entitiesToDelete)
        {
            _dbSet.RemoveRange(entitiesToDelete);
        }

        #endregion
    }
}