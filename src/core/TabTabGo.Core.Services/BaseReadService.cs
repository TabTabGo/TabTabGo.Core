using System.Data;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using LinqKit;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.Logging;
using Microsoft.OData.UriParser;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Infrastructure.Data;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services.ViewModels;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Services;

public abstract class BaseReadService<TEntity, TKey> : IBaseReadService<TEntity, TKey>
    where TEntity : class, IEntity
{
    protected const int DefaultPageSize = 50;
    protected virtual IGenericReadRepository<TEntity, TKey> CurrentRepository { get; private set; }

    protected TEntity? CurrentEntity { get; set; }
    protected ILogger Logger { get; private set; }

    /// <summary>
    /// Properties include in loading entity
    /// </summary>
    protected virtual string[] IncludeProperties { get; } = { };

    /// <summary>
    /// Properties need to be included when return detail content for entity
    /// </summary>
    protected virtual string[] DetailsProperties => IncludeProperties;

    /// <summary>
    /// Properties to be ignored when load data 
    /// </summary>
    protected virtual string[] IgnoredProperties { get; } = new string[] { };

    protected abstract Expression<Func<TEntity, bool>> GetKeyPredicate(TKey id);
    protected abstract TKey GetKey(TEntity entity);

    public BaseReadService(IGenericReadRepository<TEntity, TKey> repository,
        ILogger<BaseReadService<TEntity, TKey>> logger)
    {
        CurrentRepository = repository;
        this.Logger = logger;
        Logger.LogInformation("Created baseService for {FullName}", this.GetType().FullName);
    }

    protected virtual async Task LoadEntityAsync(TKey id,
        CancellationToken cancellationToken = default, string[]? includeProperties = null)
    {
        var predicate = GetKeyPredicate(id);
        var propertiesToInclude = new List<string>(IncludeProperties);
        if (includeProperties != null)
        {
            propertiesToInclude.AddRange(includeProperties);
        }
        //predicate = null;   //disabled the search by predicate due to bug that needs to be fixed
        if (predicate == null)
        {
            CurrentEntity = await CurrentRepository.GetByKeyAsync(id, propertiesToInclude.Distinct().ToArray(), cancellationToken);
        }
        else
        {
            CurrentEntity = await CurrentRepository.FirstOrDefaultAsync(filter: predicate,
                includeProperties: propertiesToInclude.Distinct().ToArray(), cancellationToken: cancellationToken);
        }

        PopulateAdditionalProperties(CurrentEntity);
    }

    #region Get methods

    #region Get entity

    public virtual Task<IEnumerable<TEntity>> GetList(Expression<Func<TEntity, bool>> query,
        CancellationToken cancellationToken = default)
    {
        return CurrentRepository.GetAsync(e => e, query, includeProperties: DetailsProperties,
            flags: QueryFlags.DisableTracking, cancellationToken: cancellationToken);
    }

    public virtual Task<PageList<TEntity>> GetPageList(object oDataQueryOptions,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default)
    {
        return GetPageListODataQuery(oDataQueryOptions as ODataQueryOptions<TEntity>, fixCriteria, cancellationToken);
    }

    /// <summary>
    /// Odata search on Entity
    /// </summary>
    /// <param name="query"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual Task<PageList<TEntity>> GetPageListODataQuery(ODataQueryOptions<TEntity>? query,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var includeProperties = IncludeProperties;
            var queryableEntity = GetQueryable(query, out int pageSize, out int skip, out int pageNumber,
                fixCriteria: fixCriteria, includeProperties: includeProperties);
            // Get total Count
            var totalCount = queryableEntity.Count();

            var result = new List<TEntity>();
            bool selectExpandUsed = false;
            if (skip > 0)
                queryableEntity = queryableEntity.Skip(skip);
            if (pageSize > 0)
                queryableEntity = queryableEntity.Take(pageSize);

            if (query.SelectExpand != null && !string.IsNullOrEmpty(query.SelectExpand.RawSelect))
            {
                //TODO Use select statement 

                result = queryableEntity.Select(CreateNewStatement(query.SelectExpand.RawSelect))?.ToList();
                selectExpandUsed = true;
            }
            else
            {
                result = queryableEntity.ToList();
            }

            //var listResponse = selectExpandUsed ?  : result.Select(MapOut);
            return new PageList<TEntity>(result.ToList(), totalCount, pageSize > 0 ? pageSize : DefaultPageSize,
                pageNumber);
        }, cancellationToken);
    }

    /// <summary>
    /// Get Return Entity if it is updated other wise return null
    /// </summary>
    /// <param name="id"></param>
    /// <param name="lastUpdatedDate"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<TEntity?> GetByKey(TKey id, DateTimeOffset? lastUpdatedDate = null,
        string[]? includeProperties = null, Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default)
    {
        var predicate = GetKeyPredicate(id);
        var propertiesToInclude = new List<string>(DetailsProperties);
        if (includeProperties != null)
        {
            propertiesToInclude.AddRange(includeProperties);
        }
        TEntity? entity;
        if (predicate == null)
        {
            entity = await CurrentRepository.GetByKeyAsync(id, propertiesToInclude.Distinct().ToArray(), cancellationToken);
        }
        else
        {
            ExpressionStarter<TEntity> predicateBuilder = PredicateBuilder.New<TEntity>(predicate);
            if (lastUpdatedDate.HasValue)
            {
                predicateBuilder.And(e => lastUpdatedDate.Value > e.UpdatedDate);
            }

            if (fixCriteria != null)
            {
                predicateBuilder.And(fixCriteria);
            }

            entity = await CurrentRepository.FirstOrDefaultAsync(filter: predicateBuilder,
                includeProperties: propertiesToInclude.Distinct().ToArray(), cancellationToken: cancellationToken);
        }

        if (entity != null)
        {
            PopulateAdditionalProperties(entity);
            CurrentEntity = entity;
        }

        return entity;
    }

    #endregion

    #region Get View Models

    public virtual Task<PageList<object>> GetViewModels(object oDataQueryOptions,
        Expression<Func<TEntity, bool>> fixCriteria = null,
        CancellationToken cancellationToken = default)
    {
        return GetViewModels(oDataQueryOptions as ODataQueryOptions, fixCriteria, cancellationToken);
    }

    public virtual Task<PageList<TResult>> GetViewModels<TResult>(object oDataQueryOptions,
        Expression<Func<TEntity, bool>> fixCriteria = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        return GetViewModels<TResult>(oDataQueryOptions as ODataQueryOptions, fixCriteria, cancellationToken);
    }

    public virtual Task<PageList<TResult>> GetCustomViewModels<TResult>(Func<TEntity, TResult> mapper, object oDataQueryOptions,
        Expression<Func<TEntity, bool>> fixCriteria = null,
        string[] includeProperties = null, CancellationToken cancellationToken = default)
        where TResult : class
    {
        return GetCustomViewModels<TResult>(mapper, oDataQueryOptions as ODataQueryOptions, fixCriteria,
            includeProperties, cancellationToken);
    }

    public virtual Task<PageList<TResult>> GetViewModels<TResult>(object query, Func<TEntity, TResult> mapper,
        Expression<Func<TEntity?, bool>> fixCriteria = null,
        string[] includeProperties = null, CancellationToken cancellationToken = default)
        where TResult : class => GetViewModels<TResult>(query as ODataQueryOptions, mapper, fixCriteria,
        includeProperties, cancellationToken);

    public virtual Task<PageList<TResult>> GetViewModels<TResult>(ODataQueryOptions<TEntity> query,
        Func<TEntity, TResult> mapper, Expression<Func<TEntity?, bool>>? fixCriteria = null,
        string[]? includeProperties = null, CancellationToken cancellationToken = default) where TResult : class
    {
        return Task.Run(() =>
        {
            var queryableEntity = GetQueryable(query, out int pageSize, out int skip, out int pageNumber,
                fixCriteria: fixCriteria,
                includeProperties: includeProperties ?? DetailsProperties);

            CurrentRepository.SetFlags(queryableEntity, QueryFlags.DisableTracking);
            // Get total Count
            var totalCount = queryableEntity.Count();

            var result = new List<TEntity?>();
            if (skip > 0)
                queryableEntity = queryableEntity.Skip(skip);
            if (pageSize > 0)
                queryableEntity = queryableEntity.Take(pageSize);

            result = queryableEntity.ToList();

            var page = (int)Math.Ceiling((decimal)skip / pageSize) + 1;

            return new PageList<TResult>(result.Select(mapper).ToList(), totalCount, pageSize, page);
        });
    }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <param name="mapper"></param>
    /// <param name="id"></param>
    /// <param name="lastUpdatedDate"></param>
    /// <param name="fixCriteria"></param>
    /// <param name="includeProperties"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<TResult?> GetViewModelByKey<TResult>(TKey id, Func<TEntity, TResult> mapper,
        DateTimeOffset? lastUpdatedDate = null, string[]? includeProperties = null, Expression<Func<TEntity, bool>>? fixCriteria = null, CancellationToken cancellationToken = default)
        where TResult : class
    {
        var entity = await GetByKey(id, lastUpdatedDate, includeProperties, fixCriteria, cancellationToken);
        return entity != null ? mapper(entity) : null;
    }

    #endregion

    #region OData Helper Functions

    public virtual (int pageSize, int skip, int pageNumber, ODataQuerySettings odataSettings) GetOdataProperties(
        ODataQueryOptions<TEntity>? query)
    {
        // Set page settings
        var pageSize = DefaultPageSize;
        var skip = 0;

        if (query?.Top != null && int.TryParse(query.Top.RawValue, out int top))
        {
            if (top != 0)
                pageSize = top;
        }

        if (query?.Skip != null)
        {
            skip = query.Skip.Value;
        }

        var pageNumber = (int)Math.Ceiling((decimal)skip / pageSize) + 1;

        var odataSettings = new ODataQuerySettings()
        {
            PageSize = pageSize > 0 ? pageSize : DefaultPageSize,
            HandleNullPropagation = HandleNullPropagationOption.False, EnsureStableOrdering = false
        };

        return (pageSize, skip, pageNumber, odataSettings);
    }

    public virtual string[] GetOdataPropertiesExpandProperties(ODataQueryOptions<TEntity> query,
        string[]? includeProperties = null)
    {
        var extendProperties = new List<string>(this.IncludeProperties);
        if (includeProperties != null)
        {
            extendProperties.AddRange(includeProperties);
        }

        if (query.SelectExpand != null && !string.IsNullOrEmpty(query.SelectExpand.RawExpand))
        {
            extendProperties.AddRange(query.SelectExpand.RawExpand.Split(','));
        }

        return extendProperties.Distinct().Where(p => !p.StartsWith("$")).ToArray();
    }

    public virtual OrderByQueryOption GetOdataOrderByQueryOption(ODataQueryOptions<TEntity> query)
    {
        //TODO : work around until found the correct fix   
        var context = query.Context;
        ODataQueryOptionParser oDataQueryOptionParser = new ODataQueryOptionParser(
            context.Model,
            context.ElementType,
            context.NavigationSource,
            new Dictionary<string, string> { { "$orderby", query.OrderBy.RawValue } });
        return new OrderByQueryOption(query.OrderBy.RawValue, context, oDataQueryOptionParser);
    }

    /// <summary>
    /// Execute custom query that extract from odata $filter
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="customQuery"></param>
    /// <returns></returns>
    protected virtual IQueryable<TEntity?> GetQueryable(IQueryable<TEntity?> queryable,
        IList<CustomOdataFiler> customQuery)
    {
        var gQuery = customQuery.GroupBy(g => g.FunctionName.ToLower());
        var queryBuilder = new StringBuilder();
        var selectProp = CustomQuerySelectQuery();
        queryBuilder.AppendLine(selectProp.SelectQuery);
        var queryParams = new List<object>();
        foreach (var item in gQuery)
        {
            switch (item.Key)
            {
                case "xmlexist":
                    queryBuilder.AppendLine(QueryXmlExist(selectProp.Alias, item.ToList(),
                        out List<object> parameters));
                    queryParams.AddRange(parameters);
                    break;
                default:
                    throw new NotImplementedException($"{item.Key} is not implemented");
            }
        }

        return CurrentRepository.SqlQuery(queryable, query: queryBuilder.ToString(), CommandType.Text,
            queryParams.ToArray());
    }

    /// <summary>
    /// Get custom select sql query
    /// </summary>
    /// <returns></returns>
    protected virtual (string Alias, string SelectQuery) CustomQuerySelectQuery() => ("", "");

    /// <summary>
    /// Join Sql Table query mapped to Property in OData
    /// </summary>
    /// <param name="navProperty"></param>
    /// <returns></returns>
    protected virtual (string Alias, string JoinQuery) CustomQueryJoinQuery(string navProperty)
    {
        string alias = "", joinQuery = "";

        return (alias, joinQuery);
    }

    /// <summary>
    /// Popoulate Odata filter to extract custom functions
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    protected virtual (FilterQueryOption Filter, List<CustomOdataFiler> CustomFilter) PopulateFilter(
        ODataQueryOptions<TEntity> query)
    {
        if (query.Filter != null)
        {
            var filter = query.Filter.RawValue;
            var customFilters = new List<CustomOdataFiler>();
            string pattern = @"((xmlExist|XmlExist)\((\w+[\/\w+]*),(""[\w\s\/\[\] =]+""|'[\w\s\/\[\]=]+')\))";
            CustomOdataFiler currentFilter;
            foreach (Match m in Regex.Matches(filter, pattern))
            {
                currentFilter = new CustomOdataFiler();
                if (m.Groups.Count > 2)
                {
                    currentFilter.FunctionName = m.Groups[2].Value;
                    currentFilter.Parameters = m.Groups.Cast<Group>().Select(g => g.Value).Skip(3).ToList();
                }

                customFilters.Add(currentFilter);
            }

            if (customFilters.Any())
            {
                /* Clear filter query */
                filter = Regex.Replace(filter, pattern, "");
                filter = Regex.Replace(filter, @"[or|and]*\s*\(\s*[or|and]*\s*\)", "");
                filter = Regex.Replace(filter, @"[or|and]*\s*\([\s*|or|and\s*]*\)", "");
                filter = Regex.Replace(filter, @"(?<=\()\s*[or|and]+\s*", "");
                filter = filter.Trim();
                if (filter.StartsWith("and"))
                {
                    filter = filter.Substring(3);
                }

                if (filter.StartsWith("or"))
                {
                    filter = filter.Substring(2);
                }

                FilterQueryOption odataFilter = null;
                if (!string.IsNullOrEmpty(filter))
                {
                    var queryOptions = new Dictionary<string, string>() { { "$filter", filter } };
                    var parser = new ODataQueryOptionParser(query.Context.Model, query.Context.ElementType,
                        query.Context.NavigationSource, queryOptions);
                    odataFilter = new FilterQueryOption(filter, query.Context, parser);
                }

                return (odataFilter, customFilters);
            }
        }

        return (query.Filter, null);
    }

    /// <summary>
    /// Custom odata Filter function
    /// </summary>
    /// <param name="alias"></param>
    /// <param name="list"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    protected virtual string QueryXmlExist(string alias, List<CustomOdataFiler> list, out List<object> parameters)
    {
        var propertyGroup = list.GroupBy(g => g.Parameters.FirstOrDefault()).ToList();

        parameters = new List<object>();
        int valueIndex = parameters.Count;

        var whereBuilder = new StringBuilder();
        var joinBuilder = new StringBuilder();
        var currentAlies = string.Empty;

        var addedTables = new List<string>();
        foreach (var item in propertyGroup.Where(i => i != null && i.Key != null))
        {
            currentAlies = alias;
            var navProperties = item.Key.Split('/');

            if (navProperties.Length > 0)
            {
                int navIndex = 0;
                if (navProperties.Length > 1)
                {
                    for (; navIndex < navProperties.Length - 1; navIndex++)
                    {
                        var joinProp = CustomQueryJoinQuery(navProperties[navIndex]);
                        if (!addedTables.Contains(navProperties[navIndex]))
                        {
                            joinBuilder.AppendLine(joinProp.JoinQuery);
                            addedTables.Add(navProperties[navIndex]);
                        }

                        currentAlies = joinProp.Alias;
                    }
                }

                if (item.Any(v => v.Parameters.Count > 1))
                {
                    whereBuilder.Append("( ");

                    foreach (var value in item.Where(v => v.Parameters.Count > 1))
                    {
                        //{{valueIndex++}}
                        whereBuilder.Append(
                            $" {currentAlies}.{navProperties[navIndex]}.exist({value.Parameters[navIndex]}) = 1 ");
                        whereBuilder.Append(" or ");
                        //parameters.Add(value.Parameters[1]);
                    }

                    whereBuilder.Remove(whereBuilder.Length - 5, 5);
                    whereBuilder.Append(" )");
                }

                whereBuilder.Append(" and ");
            }
        }

        whereBuilder.Remove(whereBuilder.Length - 5, 5);
        return $"{joinBuilder.ToString()} WHERE {whereBuilder.ToString()}";
    }

    #endregion

    /// <summary>
    /// Get Queryable object
    /// </summary>
    /// <param name="query"></param>
    /// <param name="pageSize"></param>
    /// <param name="skip"></param>
    /// <param name="pageNumber"></param>
    /// <returns></returns>
    public virtual IQueryable<TEntity?> GetQueryable(ODataQueryOptions<TEntity> query, out int pageSize, out int skip,
        out int pageNumber, string[] includeProperties = null, Expression<Func<TEntity, bool>>? fixCriteria = null)
    {
        ODataQuerySettings odataSettings;
        (pageSize, skip, pageNumber, odataSettings) = GetOdataProperties(query);

        //Get the expanded properties
        var extendProperties = GetOdataPropertiesExpandProperties(query, includeProperties);
        var queryableEntity = CurrentRepository.GetQueryable(extendProperties, QueryFlags.DisableTracking);
        var populatedQuery = PopulateFilter(query);
        if (populatedQuery.CustomFilter != null)
        {
            queryableEntity = GetQueryable(queryableEntity, populatedQuery.CustomFilter);
        }

        if (queryableEntity == null)
        {
            queryableEntity = CurrentRepository.GetQueryable(extendProperties, QueryFlags.DisableTracking);
        }

        //Apply filter
        if (populatedQuery.Filter != null)
            queryableEntity = populatedQuery.Filter.ApplyTo(queryableEntity, odataSettings) as IQueryable<TEntity?>;


        //Apply Order By
        if (query.OrderBy != null)
        {
            var orderBy = GetOdataOrderByQueryOption(query);
            queryableEntity = orderBy.ApplyTo(queryableEntity, odataSettings) as IQueryable<TEntity>;
        }

        if (fixCriteria != null)
        {
            queryableEntity = queryableEntity.Where(fixCriteria).AsQueryable();
        }

        return queryableEntity;
    }

    #endregion

    /// <summary>
    /// Check if entity is exist 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<bool> Exists(TKey id, CancellationToken cancellationToken = default)
    {
        var predicate = GetKeyPredicate(id);
        //predicate = null;   //disabled the search by predicate due to bug that needs to be fixed
        if (predicate == null)
        {
            return await CurrentRepository.GetByKeyAsync(id, cancellationToken: cancellationToken) != null;
        }
        else
        {
            return CurrentRepository.GetQueryable().Any(predicate);
        }
    }

    /// <summary>
    /// If need to add additional property's value or calculated properties without using view model
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    public virtual TEntity? PopulateAdditionalProperties(TEntity? entity)
    {
        return entity;
    }

    public virtual async Task<DataTable> GetViewModelsDataTable<TResult>(
        ODataQueryOptions<TEntity> query,
        Func<TEntity,TResult> mapper,
        Expression<Func<TEntity?, bool>> fixCriteria = null,
        string[] includeProperties = null,
        IList<DataColumn> columns = null,
        Func<TResult, IList<DataColumn>, DataRow> getDataRow = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        var dbResult = new DataTable(typeof(TEntity).Name);
        var result = await GetCustomViewModels<TResult>(mapper, query, fixCriteria, includeProperties,
            cancellationToken);
        columns ??= GetColumns();
        if (getDataRow == null) return dbResult;
        foreach (var item in result.Items)
        {
            dbResult.Rows.Add(getDataRow(item, columns));
        }

        return dbResult;
    }

    protected virtual IList<DataColumn> GetColumns()
    {
        return new List<DataColumn>();
    }

    public virtual async Task<Stream> ExportFile<TResult>(ExportConfiguration config, object oDataQueryOptions,
        Expression<Func<TEntity, bool>> fixCriteria = null,
        string[] includeProperties = null, CancellationToken cancellationToken = default)
        where TResult : class
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Create select function based on passed odata $select
    /// </summary>
    /// <param name="fields"></param>
    /// <returns></returns>
    protected virtual Func<TEntity, TEntity> CreateNewStatement(string fields)
    {
        // input parameter "e"
        var xParameter = Expression.Parameter(typeof(TEntity), "e");

        // new statement "new Data()"
        var xNew = Expression.New(typeof(TEntity));

        // create initializers
        var bindings = fields.Split(',').Select(e => e.Trim())
            .Select(e =>
                {
                    // property "Field1"
                    var mi = typeof(TEntity).GetProperty(e);
                    // original value "e.Field1"
                    var xOriginal = Expression.Property(xParameter, mi);
                    // set value "Field1 = e.Field1"
                    return Expression.Bind(mi, xOriginal);
                }
            );

        // initialization "new TEntity { Field1 = e.Field1, Field2 = e.Field2 }"
        var xInit = Expression.MemberInit(xNew, bindings);

        // expression "e => new TEntity { Field1 = e.Field1, Field2 = e.Field2 }"
        var lambda = Expression.Lambda<Func<TEntity, TEntity>>(xInit, xParameter);

        // compile to Func<TEntity, TEntity>
        return lambda.Compile();
    }
}