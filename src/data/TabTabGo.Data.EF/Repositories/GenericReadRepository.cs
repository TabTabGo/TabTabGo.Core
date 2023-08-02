using System.Data;
using System.Linq.Expressions;
using LinqKit.Core;
using Microsoft.EntityFrameworkCore;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Infrastructure.Data;
using TabTabGo.Core.Models;

namespace TabTabGo.Data.EF.Repositories;

public class GenericReadRepository<TEntity, TKey> : IDisposable, IGenericReadRepository<TEntity, TKey>
    where TEntity : class
{
    protected readonly DbContext _dbContext;
    protected readonly DbSet<TEntity?> _dbSet;

    public GenericReadRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = _dbContext.Set<TEntity>();
    }

    #region Get

    public async virtual Task<PageList<TResult>> GetPageListAsync<TResult>(Func<TEntity, TResult> selector,
        Expression<Func<TEntity?, bool>> filter = null, int pageNumber = 0,
        int pageSize = 20, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
        string[] includeProperties = null,
        QueryFlags? flags = null,
        CancellationToken cancellationToken = new CancellationToken()) where TResult : class
    {
        var query = GetQueryable(includeProperties, QueryFlags.DisableTracking | flags);

        if (filter != null)
        {
            query = query.Where(filter);
        }

        // Get total Count
        var totalCount = query.Count();
        if (orderBy != null)
            query = orderBy(query);

        if (pageNumber > 0)
            query = query.Skip(pageNumber * pageSize);
        if (pageSize > 0)
            query = query.Take(pageSize);
        var result = query.ToList();

        var listResponse = result.Select(selector).ToList();
        return new PageList<TResult>(listResponse.ToList(), totalCount, pageSize > 0 ? pageSize : 20, pageNumber);
    }

    // <summary>
    /// Gets the queryable.
    /// </summary>
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
            if (rowsToTake.HasValue && rowsToSkip.HasValue)
            {
                query = orderBy(query).Skip(rowsToSkip.Value).Take(rowsToTake.Value);
            }
            else if (rowsToSkip.HasValue)
            {
                query = orderBy(query).Skip(rowsToSkip.Value);
            }
            else if (rowsToTake.HasValue)
            {
                query = orderBy(query).Take(rowsToTake.Value);
            }
            else
            {
                query = orderBy(query);
            }
        }

        return selector != null ? query.Select(selector) : query.Cast<TResult>();
    }

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
        return await query.ToListAsync(cancellationToken);
    }

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
        return await query.ToListAsync(cancellationToken);
    }

    public virtual TEntity? GetByKey(object? key, string[]? includeProperties = null)
    {
        if (includeProperties == null) return _dbSet.Find(key);
        foreach (var property in includeProperties.Where(p => !p.StartsWith("$")).ToArray())
        {
            _dbSet.Include(property);
        }

        return _dbSet.Find(key);
    }

    public virtual async Task<TEntity?> GetByKeyAsync(object key, string[]? includeProperties = null,
        CancellationToken cancellationToken = default)
    {
        if (includeProperties == null) return await _dbSet.FindAsync(new object[] { key }, cancellationToken);
        foreach (var property in includeProperties.Where(p => !p.StartsWith("$")).ToArray())
        {
            _dbSet.Include(property);
        }

        return await _dbSet.FindAsync(new object[] { key }, cancellationToken);
    }

    #endregion

    #region FirstOrDefault

    public virtual TEntity FirstOrDefault(Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null, int? rowsToSkip = null
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

    public virtual Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null
        , string[] includeProperties = null
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

    public virtual TResult FirstOrDefault<TResult>(Expression<Func<TEntity, TResult>> selector
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

    public virtual Task<TResult?> FirstOrDefaultAsync<TResult>(Expression<Func<TEntity, TResult>> selector
        , Expression<Func<TEntity, bool>>? filter = null
        , Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null
        , string[]? includeProperties = null
        , int? rowsToSkip = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default) where TResult : class
    {
        var query = GetQueryable(selector: selector
            , filter: filter
            , orderBy: orderBy
            , includeProperties: includeProperties
            , rowsToSkip: rowsToSkip
            , flags: flags);

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    #endregion

    #region Count

    public virtual int Count(Expression<Func<TEntity, bool>>? filter = null
        , string[]? includes = null
        , QueryFlags? flags = null)
    {
        var query = GetQueryable(includes);
        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.IsExpandable))
        {
            query = query.AsExpandable();
        }

        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.IgnoreFixQuery))
        {
            query = query.IgnoreQueryFilters();
        }

        return filter != null ? query.Count(filter) : query.Count();
    }

    public virtual Task<int> CountAsync(Expression<Func<TEntity, bool>>? filter = null
        , string[]? includes = null
        , QueryFlags? flags = null
        , CancellationToken cancellationToken = default)
    {
        var query = GetQueryable(includes);
        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.IsExpandable))
        {
            query = query.AsExpandable();
        }

        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.IgnoreFixQuery))
        {
            query = query.IgnoreQueryFilters();
        }

        return filter != null
            ? query.CountAsync(filter, cancellationToken: cancellationToken)
            : query.CountAsync(cancellationToken: cancellationToken);
    }

    #endregion

    #region Core FUnctions

    public virtual IQueryable<TEntity?> GetQueryable(QueryFlags? flags = null)
    {
        IQueryable<TEntity?> query = flags.HasValue && flags.Value.HasFlag(QueryFlags.UseLocal)
            ? _dbSet.Local.AsQueryable<TEntity>()
            : _dbSet;
        if (flags.HasValue)
        {
            query = this.SetFlags(query, flags.Value);
        }


        return query;
    }

    public virtual IQueryable<TEntity?> GetQueryable(string[]? includeProperties, QueryFlags? flags = null)
    {
        var queryable = GetQueryable(flags);
        return IncludeProperties(queryable, includeProperties?.Where(p => !p.StartsWith("$")).ToArray());
    }

    public virtual bool IsModified(TEntity entity, string property)
    {
        return _dbContext.Entry(entity).Property(property).CurrentValue !=
               _dbContext.Entry(entity).Property(property).OriginalValue;
    }

    public virtual Task<int> ExecuteSqlCommand(string query, IDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.ExecuteSqlRawAsync(query, parameters ?? new Dictionary<string, object>(),
            cancellationToken);
    }


    public virtual IQueryable<TEntity?> IncludeProperties(IQueryable<TEntity?> queryable,
        string[]? includeProperties = null)
    {
        if (includeProperties != null)
        {
            includeProperties = includeProperties.Where(p => !p.StartsWith("$")).ToArray();
            queryable = includeProperties.Aggregate(queryable,
                (current, includeProperty) => current.Include(includeProperty));
        }

        return queryable;
    }

    public virtual void Dispose()
    {
        _dbContext?.Dispose();
    }

    public virtual IQueryable<TEntity?> SqlQuery(string query, CommandType cmdType, params object[] parameters)
    {
        return _dbSet.FromSqlRaw<TEntity>(query, parameters);
    }


    public virtual IQueryable<TEntity> SqlQuery(IQueryable<TEntity> queryable, string query, CommandType cmdType,
        params object[] parameters)
    {
        throw new NotImplementedException("IQueryable not include FromSqlRaw function");
    }

    public virtual IQueryable<TEntity?> SetFlags(IQueryable<TEntity?> queryable, QueryFlags? flags)
    {
        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.IsExpandable))
        {
            queryable = queryable.AsExpandable();
        }

        if (flags.HasValue && flags.Value.HasFlag(QueryFlags.DisableTracking))
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
}