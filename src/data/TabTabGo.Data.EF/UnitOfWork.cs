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

/// <summary>
/// Entity Framework unit of work: hands out repositories over one <see cref="DbContext"/>, owns the transaction and the save.
/// </summary>
public abstract class UnitOfWork : IUnitOfWork
{
    private readonly DbContext _context;
    private readonly ILogger<UnitOfWork> _logger;
    private readonly bool _ownsContext;

    /// <summary>Repositories created so far by "entity:key" name.</summary>
    protected readonly ConcurrentDictionary<string, object> _repositories = new();

    /// <summary>Creates a unit of work over a context the DI container owns (kept from 8.x).</summary>
    /// <param name="context">The context; must not be null</param>
    /// <param name="logger">Logger</param>
    public UnitOfWork(DbContext context, ILogger<UnitOfWork> logger)
        : this(context, logger, false)
    {
    }

    /// <summary>Creates a unit of work.</summary>
    /// <param name="context">The context; must not be null</param>
    /// <param name="logger">Logger</param>
    /// <param name="ownsContext">
    /// When true <see cref="Dispose"/> closes the connection and disposes the context. Leave false when the DI container
    /// owns the context (the usual case); pass true outside DI.
    /// </param>
    protected UnitOfWork(DbContext context, ILogger<UnitOfWork> logger, bool ownsContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);
        _context = context;
        _logger = logger;
        _ownsContext = ownsContext;
    }

    /// <summary>The context the unit of work works on.</summary>
    protected DbContext Context => _context;

    /// <summary>Sets the tracking state of an entity.</summary>
    public virtual void UpdateState<TEntity>(TEntity entity, EntityState state)
    {
        if (entity != null)
            _context.Entry(entity).State = state;
    }

    /// <inheritdoc />
    public virtual void SetEntityStateModified<TEntity, TProperty>(TEntity entity,
        Expression<Func<TEntity, TProperty>> propertyExpression) where TEntity : class where TProperty : class
    {
        _context.Entry(entity).Reference(propertyExpression!).IsModified = true;
    }

    /// <inheritdoc />
    public virtual void RemoveNavigationProperty<TEntity, TOwnerEntity>(TOwnerEntity ownerEntity, object id)
        where TEntity : class
        where TOwnerEntity : class
    {
        try
        {
            var receiverObjects = ApplyWhere(_context.Set<TEntity>(), ownerEntity.GetType().Name + "Id", id).ToList();

            foreach (var receiverObject in receiverObjects)
            {
                _context.Set<TEntity>().Remove(receiverObject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error when trying to remove navigation property. The deletion was not performed");
            throw;
        }
    }

    /// <inheritdoc />
    public virtual IReadOnlyList<EntityChange> GetChanges()
    {
        var result = new List<EntityChange>();
        foreach (var entry in _context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Modified:
                    result.Add(new EntityChange(entry.Entity, entry.State.ToString(), Snapshot(entry.OriginalValues), Snapshot(entry.CurrentValues)));
                    break;
                case EntityState.Added:
                    result.Add(new EntityChange(entry.Entity, entry.State.ToString(), new Dictionary<string, object?>(), Snapshot(entry.CurrentValues)));
                    break;
                case EntityState.Deleted:
                    result.Add(new EntityChange(entry.Entity, entry.State.ToString(), Snapshot(entry.OriginalValues), new Dictionary<string, object?>()));
                    break;
            }
        }

        return result;
    }

    private static Dictionary<string, object?> Snapshot(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues values)
    {
        // values are copied: PropertyValues is a live view of the tracker
        var snapshot = new Dictionary<string, object?>();
        foreach (var property in values.Properties)
        {
            snapshot[property.Name] = values[property];
        }

        return snapshot;
    }

    /// <inheritdoc />
    public virtual IGenericRepository<TEntity, TKey> Repository<TEntity, TKey>(string? name = null)
        where TEntity : class
    {
        var entityName = $"{(string.IsNullOrEmpty(name) ? typeof(TEntity).FullName : name)}:{typeof(TKey).FullName}";
        var repository = _repositories.GetOrAdd(entityName, _ => new GenericRepository<TEntity, TKey>(_context));
        return (IGenericRepository<TEntity, TKey>)repository;
    }

    /// <inheritdoc />
    public virtual void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        _context.Database.BeginTransaction(isolationLevel);
    }

    /// <inheritdoc />
    public virtual Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        return _context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    /// <inheritdoc />
    public virtual void Commit()
    {
        _context.SaveChanges();
        _context.Database.CommitTransaction();
    }

    /// <inheritdoc />
    public virtual async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual void Rollback()
    {
        _context.Database.RollbackTransaction();
    }

    /// <inheritdoc />
    public virtual Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return _context.Database.RollbackTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual bool HasActiveTransaction => _context.Database.CurrentTransaction != null;

    /// <inheritdoc />
    public virtual int SaveChanges()
    {
        return _context.SaveChanges();
    }

    /// <inheritdoc />
    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Saves the pending changes. EF Core has no built-in bulk save, so this saves like <see cref="SaveChangesAsync"/>;
    /// override it to plug in a bulk library.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual Task<int> SaveChangesInBulkAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual void AddOrUpdateGraph<TEntity>(TEntity entity) where TEntity : class
    {
        _context.ChangeTracker.TrackGraph(entity, e =>
        {
            var alreadyTrackedEntity = _context.ChangeTracker.Entries()
                .FirstOrDefault(entry => entry.Entity.Equals(e.Entry.Entity));
            if (alreadyTrackedEntity != null)
            {
                return;
            }

            e.Entry.State = e.Entry.IsKeySet ? EntityState.Modified : EntityState.Added;
        });
    }

    /// <summary>
    /// Closes the connection and disposes the context only when the unit of work owns it.
    /// </summary>
    public virtual void Dispose()
    {
        if (_ownsContext)
        {
            if (_context.Database.GetDbConnection().State == ConnectionState.Open)
            {
                _context.Database.CloseConnection();
            }

            _context.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    #region Helper Methods

    /// <remarks>This code from https://www.jhipster.tech/</remarks>
    private IQueryable<T> ApplyWhere<T>(IQueryable<T> source, string propertyName, object propertyValue)
        where T : class
    {
        // 1. Retrieve member access expression
        var mba = PropertyAccessorCache<T>.Get(propertyName);
        if (mba == null)
        {
            throw new InvalidOperationException($"Property '{propertyName}' does not exist on {typeof(T).Name}.");
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
            // logged once, by the caller
            throw;
        }

        // 3. Construct expression tree
        var eqe = Expression.Equal(
            mba.Body,
            Expression.Constant(value, mba.ReturnType));
        var expression = Expression.Lambda(eqe, mba.Parameters[0]);

        // 4. Construct new query
        var resultExpression = Expression.Call(
            null,
            GetMethodInfo(Queryable.Where!, source, (Expression<Func<T, bool>>?)null),
            new[] { source.Expression, Expression.Quote(expression) });
        return source.Provider.CreateQuery<T>(resultExpression);
    }

    private static MethodInfo GetMethodInfo<T1, T2, T3>(Func<T1, T2, T3> f, T1 unused1, T2 unused2)
    {
        return f.Method;
    }

    #endregion
}
