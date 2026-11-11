using System.Linq.Expressions;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TabTabGo.Core.Data;
using TabTabGo.Core.Extensions;

namespace TabTabGo.Data.EF.Repositories;

/// <summary>
/// Entity Framework implementation of <see cref="IGenericRepository{TEntity, TKey}"/>.
/// Changes are tracked only; the unit of work saves them.
/// </summary>
/// <typeparam name="TEntity">The type of the entity.</typeparam>
/// <typeparam name="TKey">The type of the key.</typeparam>
public class GenericRepository<TEntity, TKey>(DbContext dbContext)
    : GenericReadRepository<TEntity, TKey>(dbContext), IDisposable,
        IGenericRepository<TEntity, TKey>
    where TEntity : class
{
    #region Insert

    /// <inheritdoc />
    public virtual void Insert(IEnumerable<TEntity> items)
    {
        _dbSet.AddRange(items);
    }

    /// <inheritdoc />
    public virtual Task InsertAsync(IEnumerable<TEntity> items,
        CancellationToken cancellationToken = default)
    {
        return _dbSet.AddRangeAsync(items, cancellationToken);
    }

    /// <inheritdoc />
    public virtual TEntity? Insert(TEntity entity)
    {
        return _dbSet.Add(entity).Entity;
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> InsertAsync(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        var result = await _dbSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return result.Entity;
    }

    #endregion

    #region Update

    /// <inheritdoc />
    public virtual void Update(IEnumerable<TEntity> items)
    {
        _dbSet.UpdateRange(items);
    }

    /// <inheritdoc />
    public virtual Task UpdateAsync(IEnumerable<TEntity> items,
        CancellationToken cancellationToken = default)
    {
        _dbSet.UpdateRange(items);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual void Update(JsonObject entityToUpdate, Expression<Func<TEntity, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(entityToUpdate);
        ArgumentNullException.ThrowIfNull(filter);
        // materialise once: the entities are changed and then handed to EF
        var entities = _dbSet.Where(filter).ToList();
        foreach (var entity in entities)
        {
            entityToUpdate.Populate(entity);
        }

        _dbSet.UpdateRange(entities);
    }

    /// <inheritdoc />
    public virtual TEntity? Update(TEntity entityToUpdate)
    {
        var result = _dbSet.Update(entityToUpdate);
        return result.Entity;
    }

    /// <inheritdoc />
    public virtual Task UpdateAsync(JsonObject entityToUpdate, Expression<Func<TEntity, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        Update(entityToUpdate, filter);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task<TEntity?> UpdateAsync(TEntity entityToUpdate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Update(entityToUpdate));
    }

    #endregion

    #region Delete

    /// <inheritdoc />
    public virtual TEntity? Delete(TKey? key)
    {
        var entity = key == null ? null : GetByKey(key);
        return entity == null ? null : Delete(entity);
    }

    /// <inheritdoc />
    public virtual void Delete(Expression<Func<TEntity, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _dbSet.RemoveRange(_dbSet.Where(filter).ToList());
    }

    /// <inheritdoc />
    public virtual TEntity? Delete(TEntity? entityToDelete)
    {
        if (entityToDelete == null)
        {
            return null;
        }

        return _dbSet.Remove(entityToDelete).Entity;
    }

    /// <inheritdoc />
    public virtual void Delete(IQueryable<TEntity> entitiesToDelete)
    {
        ArgumentNullException.ThrowIfNull(entitiesToDelete);
        _dbSet.RemoveRange(entitiesToDelete.ToList());
    }

    #endregion
}
