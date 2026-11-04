using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using LinqKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TabTabGo.Core.Data;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Models;

namespace TabTabGo.Data.EF.Repositories;

/// <summary>
/// Entity Framework implementation of <see cref="IGenericReadRepository{TEntity, TKey}"/>.
/// The repository never disposes the <see cref="DbContext"/>: the unit of work or the DI container owns it.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TKey">Entity key type</typeparam>
public class GenericReadRepository<TEntity, TKey> : IDisposable, IGenericReadRepository<TEntity, TKey>
    where TEntity : class
{
    private static readonly MethodInfo EfPropertyMethod = typeof(Microsoft.EntityFrameworkCore.EF).GetMethod(nameof(Microsoft.EntityFrameworkCore.EF.Property))!; // justified: EF.Property is a public static method of EF

    private static readonly MethodInfo OrderByMethod = typeof(Queryable).GetMethods()
        .Single(m => m.Name == nameof(Queryable.OrderBy) && m.GetParameters().Length == 2);

    /// <summary>The context the repository works on.</summary>
    protected readonly DbContext _dbContext;

    /// <summary>The set of <typeparamref name="TEntity"/>.</summary>
    protected readonly DbSet<TEntity> _dbSet;

    /// <summary>Creates a repository over a context.</summary>
    /// <param name="dbContext">The context</param>
    public GenericReadRepository(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
        _dbSet = _dbContext.Set<TEntity>();
    }

    #region Get

    /// <inheritdoc />
    public virtual async Task<PageList<TResult>> GetPageListAsync<TResult>(Func<TEntity, TResult> selector,
        Expression<Func<TEntity, bool>>? filter = null,
        int pageNumber = 0,
        int pageSize = 20,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        string[]? includeProperties = null,
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        ArgumentNullException.ThrowIfNull(selector);
        var (query, totalCount) = await PreparePageAsync(filter, pageNumber, pageSize, orderBy, includeProperties, flags, cancellationToken)
            .ConfigureAwait(false);

        var result = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = result.Select(selector).ToList();
        return new PageList<TResult>(items, totalCount, pageSize > 0 ? pageSize : 20, pageNumber + 1);
    }

    /// <inheritdoc />
    public virtual async Task<PageList<TResult>> GetProjectedPageListAsync<TResult>(
        Expression<Func<TEntity, TResult>> selector,
        Expression<Func<TEntity, bool>>? filter = null,
        int pageNumber = 0,
        int pageSize = 20,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        string[]? includeProperties = null,
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        ArgumentNullException.ThrowIfNull(selector);
        var (query, totalCount) = await PreparePageAsync(filter, pageNumber, pageSize, orderBy, includeProperties, flags, cancellationToken)
            .ConfigureAwait(false);

        var items = await query.Select(selector).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PageList<TResult>(items, totalCount, pageSize > 0 ? pageSize : 20, pageNumber + 1);
    }

    private async Task<(IQueryable<TEntity> Query, int TotalCount)> PreparePageAsync(
        Expression<Func<TEntity, bool>>? filter,
        int pageNumber,
        int pageSize,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy,
        string[]? includeProperties,
        QueryFlags? flags,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNumber);
        // paged reads are untracked unless the caller asks for tracking
        var effective = flags ?? 0;
        var query = GetQueryable(includeProperties, effective.HasFlag(QueryFlags.EnableTracking) ? effective : effective | QueryFlags.DisableTracking);

        if (filter != null)
        {
            query = query.Where(filter);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // paging without an order is not deterministic, so fall back to the primary key
        query = orderBy != null ? orderBy(query) : OrderByPrimaryKey(query);

        if (pageSize > 0)
        {
            if (pageNumber > 0)
            {
                query = query.Skip(pageNumber * pageSize);
            }

            query = query.Take(pageSize);
        }

        return (query, totalCount);
    }

    /// <summary>
    /// Gets the queryable.
    /// </summary>
    /// <param name="selector">The projection.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="orderBy">The order by.</param>
    /// <param name="includeProperties">The include properties.</param>
    /// <param name="rowsToTake">The rows to take.</param>
    /// <param name="rowsToSkip">The rows to skip.</param>
    /// <param name="flags"></param>
    /// <returns></returns>
    public virtual IQueryable<TResult> GetQueryable<TResult>(
        Expression<Func<TEntity, TResult>>? selector = null
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null) where TResult : class
    {
        var query = GetQueryable(includeProperties, flags);

        if (filter != null)
        {
            query = query.Where(filter);
        }

        if (orderBy != null)
        {
            query = orderBy(query);
        }
        else if (rowsToSkip.HasValue || rowsToTake.HasValue)
        {
            // skip/take need a stable order to mean anything
            query = OrderByPrimaryKey(query);
        }

        if (rowsToSkip.HasValue)
        {
            query = query.Skip(rowsToSkip.Value);
        }

        if (rowsToTake.HasValue)
        {
            query = query.Take(rowsToTake.Value);
        }

        return selector != null ? query.Select(selector) : query.Cast<TResult>();
    }

    /// <inheritdoc />
    public virtual IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null)
    {
        var query = GetQueryable<TEntity>(
            selector: e => e
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , rowsToTake: rowsToTake
            , flags: flags);

        return query.ToList();
    }

    /// <inheritdoc />
    public virtual IEnumerable<TResult> Get<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null) where TResult : class
    {
        var query = GetQueryable<TResult>(
            selector: selector
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , rowsToTake: rowsToTake
            , flags: flags);

        return query.ToList();
    }

    /// <inheritdoc />
    public virtual async Task<IEnumerable<TEntity>> GetAsync(Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default)
    {
        var query = GetQueryable<TEntity>(
            selector: e => e
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , rowsToTake: rowsToTake
            , flags: flags);
        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IEnumerable<TResult>> GetAsync<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToTake = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default) where TResult : class
    {
        var query = GetQueryable<TResult>(
            selector: selector
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , rowsToTake: rowsToTake
            , flags: flags);
        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual TEntity? GetByKey(TKey key, string[]? includeProperties = null)
    {
        var includes = includeProperties?.Where(p => !p.StartsWith('$')).ToArray();
        if (includes == null || includes.Length == 0) return _dbSet.Find(key);

        return IncludeProperties(_dbSet, includes).FirstOrDefault(KeyPredicate(key));
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByKeyAsync(TKey key, string[]? includeProperties = null,
        CancellationToken cancellationToken = default)
    {
        var includes = includeProperties?.Where(p => !p.StartsWith('$')).ToArray();
        if (includes == null || includes.Length == 0)
        {
            return await _dbSet.FindAsync(new object?[] { key }, cancellationToken).ConfigureAwait(false);
        }

        return await IncludeProperties(_dbSet, includes)
            .FirstOrDefaultAsync(KeyPredicate(key), cancellationToken)
            .ConfigureAwait(false);
    }

    #endregion

    #region FirstOrDefault

    /// <inheritdoc />
    public virtual TEntity? FirstOrDefault(Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null, int? rowsToSkip = null
        , QueryFlags? flags = null)
    {
        var query = GetQueryable<TEntity>(
            selector: e => e
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , flags: flags);

        return query.FirstOrDefault();
    }

    /// <inheritdoc />
    public virtual Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default)
    {
        var query = GetQueryable<TEntity>(
            selector: e => e
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , flags: flags);

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual TResult? FirstOrDefault<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null, int? rowsToSkip = null
        , QueryFlags? flags = null) where TResult : class
    {
        var query = GetQueryable<TResult>(selector: selector
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , flags: flags);

        return query.FirstOrDefault();
    }

    /// <inheritdoc />
    public virtual Task<TResult?> FirstOrDefaultAsync<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default) where TResult : class
    {
        var query = GetQueryable<TResult>(selector: selector
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , flags: flags);

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    #endregion

    #region Count

    /// <inheritdoc />
    public virtual int Count(Expression<Func<TEntity, bool>>? filter = null
        , string[]? includes = null
        , QueryFlags? flags = null)
    {
        var query = GetQueryable(includes, flags);
        return filter != null ? query.Count(filter) : query.Count();
    }

    /// <inheritdoc />
    public virtual Task<int> CountAsync(Expression<Func<TEntity, bool>>? filter = null
        , string[]? includes = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default)
    {
        var query = GetQueryable(includes, flags);
        return filter != null
            ? query.CountAsync(filter, cancellationToken)
            : query.CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? filter = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default)
    {
        var query = GetQueryable(flags);
        return filter != null
            ? query.AnyAsync(filter, cancellationToken)
            : query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Provider is IAsyncQueryProvider
            ? query.CountAsync(cancellationToken)
            : Task.FromResult(query.Count());
    }

    /// <inheritdoc />
    public virtual Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Provider is IAsyncQueryProvider
            ? query.ToListAsync(cancellationToken)
            : Task.FromResult(query.ToList());
    }

    #endregion

    #region Core Functions

    /// <inheritdoc />
    public virtual IQueryable<TEntity> GetQueryable(QueryFlags? flags = null)
    {
        IQueryable<TEntity> query = flags.HasValue && flags.Value.HasFlag(QueryFlags.UseLocal)
            ? _dbSet.Local.AsQueryable()
            : _dbSet;
        if (flags.HasValue)
        {
            query = SetFlags(query, flags.Value);
        }

        return query;
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> GetQueryable(string[]? includeProperties, QueryFlags? flags = null)
    {
        var queryable = GetQueryable(flags);
        return IncludeProperties(queryable, includeProperties?.Where(p => !p.StartsWith('$')).ToArray());
    }

    /// <inheritdoc />
    public virtual bool IsModified(TEntity entity, string property)
    {
        var propertyEntry = _dbContext.Entry(entity).Property(property);
        return !Equals(propertyEntry.CurrentValue, propertyEntry.OriginalValue);
    }

    /// <inheritdoc />
    public virtual async Task<int> ExecuteSqlCommand(string query, IDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var dbParameters = new List<DbParameter>();
        if (parameters != null)
        {
            // named parameters must be provider parameters; a dictionary passed as one value is not understood by the provider
            using var command = _dbContext.Database.GetDbConnection().CreateCommand();
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                dbParameters.Add(parameter);
            }
        }

        return await _dbContext.Database.ExecuteSqlRawAsync(query, dbParameters, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> IncludeProperties(IQueryable<TEntity> queryable,
        string[]? includeProperties = null)
    {
        ArgumentNullException.ThrowIfNull(queryable);
        if (includeProperties != null)
        {
            includeProperties = includeProperties.Where(p => !p.StartsWith('$')).ToArray();
            queryable = includeProperties.Aggregate(queryable,
                (current, includeProperty) => current.Include(includeProperty));
        }

        return queryable;
    }

    /// <summary>
    /// Does nothing: the <see cref="DbContext"/> is owned by the unit of work or the DI container.
    /// </summary>
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> SqlQuery(string query, CommandType cmdType, params object[] parameters)
    {
        return _dbSet.FromSqlRaw(query, parameters);
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> SqlQuery(IQueryable<TEntity> queryable, string query, CommandType cmdType,
        params object[] parameters)
    {
        throw new NotSupportedException("IQueryable not include FromSqlRaw function");
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> SetFlags(IQueryable<TEntity> queryable, QueryFlags? flags)
    {
        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.IsExpandable))
        {
            queryable = queryable.AsExpandable();
        }

        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.DisableTracking) && !flags.Value.HasFlag(QueryFlags.EnableTracking))
        {
            queryable = queryable.AsNoTracking();
        }

        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.IgnoreFixQuery))
        {
            queryable = queryable.IgnoreQueryFilters();
        }

        return queryable;
    }

    #endregion

    #region Key helpers

    /// <inheritdoc />
    public virtual Expression<Func<TEntity, bool>>? GetKeysPredicate(IEnumerable<TKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keyProperty = SingleKeyProperty();
        if (keyProperty.ClrType != typeof(TKey))
        {
            return null;
        }

        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var access = Expression.Call(EfPropertyMethod.MakeGenericMethod(keyProperty.ClrType), parameter, Expression.Constant(keyProperty.Name));
        var list = keys.Distinct().ToList();
        var contains = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(TKey) }, Expression.Constant(list), access);
        return Expression.Lambda<Func<TEntity, bool>>(contains, parameter);
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> ApplyKeyOrder(IQueryable<TEntity> queryable)
    {
        ArgumentNullException.ThrowIfNull(queryable);
        return OrderByPrimaryKey(queryable);
    }

    /// <summary>
    /// Builds <c>e =&gt; e.Key == key</c> from the EF key metadata. Only single-property keys are supported.
    /// </summary>
    /// <param name="key">The key value</param>
    protected virtual Expression<Func<TEntity, bool>> KeyPredicate(TKey key)
    {
        var keyProperty = SingleKeyProperty();
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var access = Expression.Call(EfPropertyMethod.MakeGenericMethod(keyProperty.ClrType), parameter, Expression.Constant(keyProperty.Name));
        // a closure keeps the key a query parameter so the query plan is cached
        Expression value = Expression.Property(Expression.Constant(new KeyHolder(key)), nameof(KeyHolder.Value));
        value = Expression.Convert(value, keyProperty.ClrType);
        return Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(access, value), parameter);
    }

    private sealed record KeyHolder(TKey? Value);

    private Microsoft.EntityFrameworkCore.Metadata.IProperty SingleKeyProperty()
    {
        var primaryKey = _dbContext.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()
                         ?? throw new InvalidOperationException($"{typeof(TEntity).Name} has no primary key.");
        return primaryKey.Properties.Count == 1
            ? primaryKey.Properties[0]
            : throw new NotSupportedException($"{typeof(TEntity).Name} has a composite key; a single key value cannot identify it.");
    }

    private IQueryable<TEntity> OrderByPrimaryKey(IQueryable<TEntity> query)
    {
        var primaryKey = _dbContext.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey();
        if (primaryKey == null || primaryKey.Properties.Count == 0)
        {
            return query;
        }

        IOrderedQueryable<TEntity>? ordered = null;
        foreach (var keyProperty in primaryKey.Properties)
        {
            var parameter = Expression.Parameter(typeof(TEntity), "e");
            var access = Expression.Call(EfPropertyMethod.MakeGenericMethod(keyProperty.ClrType), parameter, Expression.Constant(keyProperty.Name));
            var lambda = Expression.Lambda(access, parameter);
            ordered = ordered == null
                ? (IOrderedQueryable<TEntity>)OrderByMethod.MakeGenericMethod(typeof(TEntity), keyProperty.ClrType).Invoke(null, new object[] { query, lambda })! // justified: the reflected LINQ method always returns an IOrderedQueryable
                : (IOrderedQueryable<TEntity>)ThenByMethod.MakeGenericMethod(typeof(TEntity), keyProperty.ClrType).Invoke(null, new object[] { ordered, lambda })!; // justified: the reflected LINQ method always returns an IOrderedQueryable
        }

        return ordered!; // justified: the primary key has at least one property, so the loop assigned it
    }

    private static readonly MethodInfo ThenByMethod = typeof(Queryable).GetMethods()
        .Single(m => m.Name == nameof(Queryable.ThenBy) && m.GetParameters().Length == 2);

    #endregion
}
