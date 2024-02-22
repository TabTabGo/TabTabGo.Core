using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Data;
using TabTabGo.Core.Extensions;
using TabTabGo.Data.EF.Repositories;

namespace TabTabGo.Data.EF;

public abstract class UnitOfWork(DbContext? context, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    // check if context is null then throw exception

    protected readonly ConcurrentDictionary<string, object> _repositories = new ConcurrentDictionary<string, object>();

    public virtual void UpdateState<TEntity>(TEntity entity, EntityState state)
    {
        if (entity != null && context != null)
            context.Entry(entity).State = state;
    }

    public virtual void SetEntityStateModified<TEntity, TProperty>(TEntity entity,
        Expression<Func<TEntity, TProperty>> propertyExpression) where TEntity : class where TProperty : class
    {
        if (context != null) context.Entry(entity).Reference(propertyExpression!).IsModified = true;
    }

    public virtual void RemoveNavigationProperty<TEntity, TOwnerEntity>(TOwnerEntity ownerEntity, object id)
        where TEntity : class
        where TOwnerEntity : class
    {
        try
        {
            var receiverObjects = ApplyWhere(context?.Set<TEntity>(), ownerEntity.GetType().Name + "Id", id);

            foreach (TEntity receiverObject in receiverObjects)
            {
                context?.Set<TEntity>().Remove(receiverObject);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error when trying to remove navigation property. The deletion was not performed");
        }
    }

    public virtual dynamic? GetChanges()
    {
        List<dynamic> result = new List<dynamic>();
        if (context == null) return result;
        var changes = context.ChangeTracker.Entries();
        foreach (var entry in changes)
        {
            switch (entry.State)
            {
                case EntityState.Modified:
                    result.Add(new { entry.State, entry.OriginalValues, entry.CurrentValues });
                    break;
                case EntityState.Added:
                    result.Add(new { entry.State, entry.CurrentValues });
                    break;
                case EntityState.Deleted:
                    result.Add(new { entry.State, entry.OriginalValues });
                    break;
                case EntityState.Detached:
                    break;
                case EntityState.Unchanged:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return result;
    }

    public virtual IGenericRepository<TEntity, TKey> Repository<TEntity, TKey>(string? name = null)
        where TEntity : class
    {
        var entityName = string.IsNullOrEmpty(name) ? typeof(TEntity).FullName : name;
        if (entityName != null && _repositories.TryGetValue(entityName, out var repository))
        {
            return (IGenericRepository<TEntity, TKey>)repository;
        }
        repository = new GenericRepository<TEntity, TKey>(context);
        if (entityName != null) _repositories.TryAdd(entityName, repository);
        return (IGenericRepository<TEntity, TKey>)repository;
    }

    public virtual void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        context?.Database.BeginTransaction();
    }

    public virtual void Commit()
    {
        context?.SaveChanges();
        context?.Database.CommitTransaction();
    }

    public virtual void Rollback()
    {
        context?.Database.RollbackTransaction();
    }

    public virtual int SaveChanges()
    {
        return context?.SaveChanges() ?? 0;
    }

    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return context?.SaveChangesAsync(cancellationToken);
    }

    public virtual Task<int> SaveChangesInBulkAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public virtual void AddOrUpdateGraph<TEntity>(TEntity entity) where TEntity : class
    {
        context?.ChangeTracker.TrackGraph(entity, e =>
        {
            var alreadyTrackedEntity = context.ChangeTracker.Entries()
                .FirstOrDefault(entry => entry.Entity.Equals(e.Entry.Entity));
            if (alreadyTrackedEntity != null)
            {
                return;
            }

            e.Entry.State = e.Entry.IsKeySet ? EntityState.Modified : EntityState.Added;
        });
    }

    public virtual void Dispose()
    {
        // make sure to call connection close if it is open
        if (context != null && context.Database.GetDbConnection().State == ConnectionState.Open)
        {
            context.Database.CloseConnection();
        }

        context?.Dispose();
    }

    #region Helper Methods

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>This code from https://www.jhipster.tech/</remarks>
    /// <param name="source"></param>
    /// <param name="propertyName"></param>
    /// <param name="propertyValue"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="NullReferenceException"></exception>
    private IQueryable<T> ApplyWhere<T>(IQueryable<T> source, string propertyName, object propertyValue)
        where T : class
    {
        // 1. Retrieve member access expression
        var mba = PropertyAccessorCache<T>.Get(propertyName);
        if (mba == null)
        {
            var ex = new NullReferenceException();
            logger.LogError(ex, $"Error when trying to get the property, it doesn't exist");
            throw ex;
        }

        // 2. Try converting value to correct type
        object value;
        try
        {
            value = Convert.ChangeType(propertyValue, mba.ReturnType);
        }
        catch (SystemException ex) when (
            ex is InvalidCastException ||
            ex is FormatException ||
            ex is OverflowException ||
            ex is ArgumentNullException)
        {
            logger.LogError(ex, $"Error when trying to convert type of property value with type of property");
            throw;
        }

        // 3. Construct expression tree
        var eqe = Expression.Equal(
            mba.Body,
            Expression.Constant(value, mba.ReturnType));
        var expression = Expression.Lambda(eqe, mba.Parameters[0]);

        // 4. Construct new query
        MethodCallExpression resultExpression = Expression.Call(
            null,
            GetMethodInfo(Queryable.Where!, source, (Expression<Func<T, bool>>?)null),
            new Expression[] { source.Expression, Expression.Quote(expression) });
        return source.Provider.CreateQuery<T>(resultExpression);
    }

    private MethodInfo GetMethodInfo<T1, T2, T3>(Func<T1, T2, T3> f, T1 unused1, T2 unused2)
    {
        return f.Method;
    }

    #endregion
}