using System.Data;
using System.Linq.Expressions;
using System.Text;
using LinqKit;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.Logging;
using Microsoft.OData.UriParser;
using TabTabGo.Core.Data;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services.OData;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Services;

/// <summary>
/// Read side of an entity service: lists, pages, OData queries, view models and exports over a repository.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TKey">Entity key type</typeparam>
public abstract partial class BaseReadService<TEntity, TKey> : IBaseReadService<TEntity, TKey>
    where TEntity : class, IEntity
{
    /// <summary>Page size used when a request does not ask for one.</summary>
    protected const int DefaultPageSize = 50;

    private static readonly string[] CustomFunctions = { "xmlExist" };

    /// <summary>The repository the service reads from.</summary>
    protected virtual IGenericReadRepository<TEntity, TKey> CurrentRepository { get; private set; }

    /// <summary>The entity loaded by the last load.</summary>
    protected TEntity? CurrentEntity { get; set; }

    /// <summary>Logger.</summary>
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
    protected virtual string[]? IgnoredProperties { get; } = new string[] { };

    /// <summary>Most keys a bulk request may name.</summary>
    public const int MaxBulkKeys = 500;

    /// <inheritdoc />
    public abstract Expression<Func<TEntity, bool>> GetKeyPredicate(TKey id);

    /// <summary>
    /// The predicate that finds one entity: <see cref="GetKeyPredicate"/>, or one built from the store's key metadata when
    /// that returns null. A scope can only be applied through a predicate, so when neither exists this throws.
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <exception cref="InvalidOperationException">No predicate can be built</exception>
    protected Expression<Func<TEntity, bool>> ResolveKeyPredicate(TKey id) =>
        GetKeyPredicate(id)
        ?? CurrentRepository.GetKeysPredicate(new[] { id })
        ?? throw new InvalidOperationException(
            $"{GetType().Name}.GetKeyPredicate returned null and the repository cannot build a key predicate, so a lookup cannot be scoped.");

    /// <summary>
    /// True makes <see cref="KeysPredicate"/> (bulk delete, <c>CountByKeys</c>) use this service's <c>GetKeyPredicate</c> for every key
    /// instead of the store's primary-key <c>IN</c> list. Override it when <c>GetKeyPredicate</c> is not "primary key equals key".
    /// </summary>
    protected virtual bool PreferServiceKeyPredicate => false;

    /// <summary>One predicate for several keys: the store's <c>IN</c> list when it can build one, otherwise an OR of the key predicates.</summary>
    /// <param name="keys">The keys (at most <see cref="MaxBulkKeys"/>)</param>
    /// <exception cref="ApiException">Too many keys (400)</exception>
    protected Expression<Func<TEntity, bool>> KeysPredicate(IReadOnlyCollection<TKey> keys)
    {
        if (keys.Count > MaxBulkKeys)
        {
            throw new ApiException($"At most {MaxBulkKeys} keys can be sent in one request.", errorNumber: 3003,
                statusCode: System.Net.HttpStatusCode.BadRequest, code: "TOO_MANY_IDS");
        }

        // the store's list is built on the primary key: a service whose GetKeyPredicate is something else (a lookup by code)
        // opts out, and a composite key has no list at all
        var contains = PreferServiceKeyPredicate ? null : CurrentRepository.GetKeysPredicate(keys);
        if (contains != null)
        {
            return contains;
        }

        var predicate = PredicateBuilder.New<TEntity>(false);
        foreach (var key in keys)
        {
            predicate = predicate.Or(ResolveKeyPredicate(key));
        }

        return predicate;
    }

    /// <inheritdoc />
    public virtual async Task<int> CountByKeys(IEnumerable<TKey> ids, Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
        {
            return 0;
        }

        var predicate = PredicateBuilder.New(KeysPredicate(keys));
        if (fixCriteria != null)
        {
            predicate = predicate.And(fixCriteria);
        }

        return await CurrentRepository.CountAsync(predicate, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public abstract TKey GetKey(TEntity entity);

    /// <summary>Creates the service.</summary>
    /// <param name="repository">Repository</param>
    /// <param name="logger">Logger</param>
    public BaseReadService(IGenericReadRepository<TEntity, TKey> repository,
        ILogger<BaseReadService<TEntity, TKey>> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);
        CurrentRepository = repository;
        Logger = logger;
        Logger.LogDebug("Created baseService for {FullName}", GetType().FullName);
    }

    /// <summary>Loads the entity into <see cref="CurrentEntity"/> (null when it does not exist).</summary>
    /// <param name="id">Entity key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="includeProperties">Extra properties to include</param>
    protected virtual async Task LoadEntityAsync(TKey id,
        CancellationToken cancellationToken = default, string[]? includeProperties = null)
    {
        var predicate = ResolveKeyPredicate(id);
        var propertiesToInclude = new List<string>(IncludeProperties);
        if (includeProperties != null)
        {
            propertiesToInclude.AddRange(includeProperties);
        }

        CurrentEntity = await CurrentRepository.FirstOrDefaultAsync(filter: predicate,
            includeProperties: propertiesToInclude.Distinct().ToArray(), cancellationToken: cancellationToken);

        PopulateAdditionalProperties(CurrentEntity);
    }

    #region Get methods

    #region Get entity

    /// <inheritdoc />
    public virtual Task<IEnumerable<TEntity>> GetList(Expression<Func<TEntity, bool>> query,
        string[]? includeProperties = null, QueryFlags? flags = null, CancellationToken cancellationToken = default)
    {
        var propertiesToInclude = new List<string>(IncludeProperties);
        if (includeProperties != null)
        {
            propertiesToInclude.AddRange(includeProperties);
        }

        return CurrentRepository.GetAsync(filter: query, includeProperties: propertiesToInclude.ToArray(), flags: flags,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<PageList<TEntity>> GetPageList(Expression<Func<TEntity, bool>> query,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, int page = 0, int pageSize = 25,
        string[]? includeProperties = null, QueryFlags? flags = null, CancellationToken cancellationToken = default)
    {
        var propertiesToInclude = new List<string>(IncludeProperties);
        if (includeProperties != null)
        {
            propertiesToInclude.AddRange(includeProperties);
        }

        return CurrentRepository.GetPageListAsync(x => x, query, page, pageSize, orderBy,
            propertiesToInclude.ToArray(), flags, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<PageList<TEntity>> GetPageList(object oDataQueryOptions,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        string[]? includeProperties = null,
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default)
    {
        return GetPageListODataQuery(AsODataOptions(oDataQueryOptions), fixCriteria, includeProperties, flags, cancellationToken);
    }

    /// <summary>
    /// Odata search on Entity
    /// </summary>
    /// <param name="query">The OData options</param>
    /// <param name="fixCriteria">Scope criteria always applied</param>
    /// <param name="includeProperties">Extra properties to include</param>
    /// <param name="flags">Query flags</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<PageList<TEntity>> GetPageListODataQuery(ODataQueryOptions<TEntity>? query,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        string[]? includeProperties = null,
        QueryFlags? flags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var propertiesToInclude = IncludeProperties.Concat(includeProperties ?? Array.Empty<string>()).Distinct().ToArray();
        var queryableEntity = GetQueryable(query, out var pageSize, out var skip, out var pageNumber,
            fixCriteria: fixCriteria, includeProperties: propertiesToInclude, flags: flags);

        var totalCount = await CurrentRepository.CountAsync(queryableEntity, cancellationToken);

        if (skip > 0)
            queryableEntity = queryableEntity.Skip(skip);
        if (pageSize > 0)
            queryableEntity = queryableEntity.Take(pageSize);

        if (query.SelectExpand != null && !string.IsNullOrEmpty(query.SelectExpand.RawSelect))
        {
            queryableEntity = queryableEntity.Select(CreateSelectExpression(query.SelectExpand.RawSelect));
        }

        var result = await CurrentRepository.ToListAsync(queryableEntity, cancellationToken);
        return new PageList<TEntity>(result, totalCount, pageSize > 0 ? pageSize : DefaultPageSize, pageNumber);
    }

    /// <summary>
    /// Gets an entity by key. With <paramref name="lastUpdatedDate"/> the entity is returned only when it has <b>not</b> been
    /// updated since that date (<c>lastUpdatedDate &gt; entity.UpdatedDate</c>); otherwise the result is null.
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <param name="lastUpdatedDate">The entity is returned only when it was last updated before this date</param>
    /// <param name="includeProperties">Extra properties to include</param>
    /// <param name="fixCriteria">Scope criteria always applied</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<TEntity?> GetByKey(TKey id, DateTimeOffset? lastUpdatedDate = null,
        string[]? includeProperties = null, Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default)
    {
        var propertiesToInclude = new List<string>(DetailsProperties);
        if (includeProperties != null)
        {
            propertiesToInclude.AddRange(includeProperties);
        }

        var predicateBuilder = PredicateBuilder.New(ResolveKeyPredicate(id));
        if (lastUpdatedDate.HasValue)
        {
            var date = lastUpdatedDate.Value;
            predicateBuilder = predicateBuilder.And(e => date > e.UpdatedDate);
        }

        if (fixCriteria != null)
        {
            predicateBuilder = predicateBuilder.And(fixCriteria);
        }

        var entity = await CurrentRepository.FirstOrDefaultAsync(filter: predicateBuilder,
            includeProperties: propertiesToInclude.Distinct().ToArray(), cancellationToken: cancellationToken);

        if (entity != null)
        {
            PopulateAdditionalProperties(entity);
            CurrentEntity = entity;
        }

        return entity;
    }

    #endregion

    #region Get View Models

    /// <inheritdoc />
    public virtual async Task<PageList<object>> GetViewModels(object oDataQueryOptions,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default)
    {
        return await GetViewModels<object>(AsODataOptions(oDataQueryOptions), entity => entity, fixCriteria, null, cancellationToken);
    }

    /// <summary>
    /// Get view model paging list where the view model type is the entity (or a base type of it).
    /// </summary>
    /// <typeparam name="TResult">Result type; must be assignable from the entity type</typeparam>
    /// <param name="oDataQueryOptions">The OData options</param>
    /// <param name="fixCriteria">Scope criteria always applied</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual Task<PageList<TResult>> GetViewModels<TResult>(object oDataQueryOptions,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        if (!typeof(TResult).IsAssignableFrom(typeof(TEntity)))
        {
            throw new InvalidOperationException(
                $"{typeof(TResult).Name} is not assignable from {typeof(TEntity).Name}; pass a mapper.");
        }

        return GetViewModels<TResult>(AsODataOptions(oDataQueryOptions), entity => (TResult)(object)entity, fixCriteria, null,
            cancellationToken);
    }

    /// <summary>
    /// Get view model paging list using a mapper.
    /// </summary>
    /// <typeparam name="TResult">View model type</typeparam>
    /// <param name="mapper">Maps an entity to the view model</param>
    /// <param name="oDataQueryOptions">The OData options</param>
    /// <param name="fixCriteria">Scope criteria always applied</param>
    /// <param name="includeProperties">Extra properties to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual Task<PageList<TResult>> GetCustomViewModels<TResult>(Func<TEntity, TResult> mapper,
        object oDataQueryOptions,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        string[]? includeProperties = null, CancellationToken cancellationToken = default)
        where TResult : class
    {
        return GetViewModels<TResult>(AsODataOptions(oDataQueryOptions), mapper, fixCriteria, includeProperties,
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<PageList<TResult>> GetViewModels<TResult>(object oDataQueryOptions, Func<TEntity, TResult> mapper,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        string[]? includeProperties = null, CancellationToken cancellationToken = default)
        where TResult : class => GetViewModels<TResult>(AsODataOptions(oDataQueryOptions), mapper, fixCriteria,
        includeProperties, cancellationToken);

    /// <summary>
    /// Get view model paging list using a mapper.
    /// </summary>
    /// <typeparam name="TResult">View model type</typeparam>
    /// <param name="query">The OData options</param>
    /// <param name="mapper">Maps an entity to the view model</param>
    /// <param name="fixCriteria">Scope criteria always applied</param>
    /// <param name="includeProperties">Extra properties to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<PageList<TResult>> GetViewModels<TResult>(ODataQueryOptions<TEntity> query,
        Func<TEntity, TResult> mapper, Expression<Func<TEntity, bool>>? fixCriteria = null,
        string[]? includeProperties = null, CancellationToken cancellationToken = default) where TResult : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(mapper);
        var queryableEntity = GetQueryable(query, out var pageSize, out var skip, out var pageNumber,
            fixCriteria: fixCriteria,
            includeProperties: includeProperties ?? DetailsProperties,
            flags: QueryFlags.DisableTracking);

        var totalCount = await CurrentRepository.CountAsync(queryableEntity, cancellationToken);

        if (skip > 0)
            queryableEntity = queryableEntity.Skip(skip);
        if (pageSize > 0)
            queryableEntity = queryableEntity.Take(pageSize);

        var result = await CurrentRepository.ToListAsync(queryableEntity, cancellationToken);
        return new PageList<TResult>(result.Select(mapper).ToList(), totalCount, pageSize, pageNumber);
    }

    /// <inheritdoc />
    public virtual async Task<TResult?> GetViewModelByKey<TResult>(TKey id, Func<TEntity, TResult> mapper,
        DateTimeOffset? lastUpdatedDate = null, string[]? includeProperties = null,
        Expression<Func<TEntity, bool>>? fixCriteria = null, CancellationToken cancellationToken = default)
        where TResult : class
    {
        var entity = await GetByKey(id, lastUpdatedDate, includeProperties, fixCriteria, cancellationToken);
        return entity != null ? mapper(entity) : null;
    }

    #endregion

    #region OData Helper Functions

    /// <summary>
    /// Casts the object a caller passed to the typed OData options.
    /// </summary>
    /// <param name="oDataQueryOptions">The options object</param>
    /// <exception cref="ArgumentNullException">The options are null</exception>
    /// <exception cref="ArgumentException">The object is not an <see cref="ODataQueryOptions{TEntity}"/></exception>
    protected static ODataQueryOptions<TEntity> AsODataOptions(object? oDataQueryOptions)
    {
        ArgumentNullException.ThrowIfNull(oDataQueryOptions);
        return oDataQueryOptions as ODataQueryOptions<TEntity>
               ?? throw new ArgumentException(
                   $"Expected {nameof(ODataQueryOptions)}<{typeof(TEntity).Name}> but got {oDataQueryOptions.GetType().Name}.",
                   nameof(oDataQueryOptions));
    }

    /// <summary>Reads page size, skip and page number (1-based) from the OData options.</summary>
    /// <param name="query">The OData options</param>
    public virtual (int pageSize, int skip, int pageNumber, ODataQuerySettings odataSettings) GetOdataProperties(
        ODataQueryOptions<TEntity>? query)
    {
        // Set page settings
        var pageSize = DefaultPageSize;
        var skip = 0;

        if (query?.Top != null && int.TryParse(query.Top.RawValue, out var top) && top > 0)
        {
            pageSize = top;
        }

        if (query?.Skip != null)
        {
            skip = query.Skip.Value;
        }

        var pageNumber = (int)Math.Ceiling((decimal)skip / pageSize) + 1;

        var odataSettings = new ODataQuerySettings()
        {
            PageSize = pageSize,
            HandleNullPropagation = HandleNullPropagationOption.False, EnsureStableOrdering = false
        };

        return (pageSize, skip, pageNumber, odataSettings);
    }

    /// <summary>Properties to include for an OData request: the service defaults, the given ones and <c>$expand</c>.</summary>
    /// <param name="query">The OData options</param>
    /// <param name="includeProperties">Extra properties</param>
    public virtual string[] GetOdataPropertiesExpandProperties(ODataQueryOptions<TEntity> query,
        string[]? includeProperties = null)
    {
        var extendProperties = new List<string>(IncludeProperties);
        if (includeProperties != null)
        {
            extendProperties.AddRange(includeProperties);
        }

        if (query.SelectExpand != null && !string.IsNullOrEmpty(query.SelectExpand.RawExpand))
        {
            extendProperties.AddRange(query.SelectExpand.RawExpand.Split(','));
        }

        return extendProperties.Distinct().Where(p => !p.StartsWith('$')).ToArray();
    }

    /// <summary>Re-parses <c>$orderby</c> so property names match case-insensitively.</summary>
    /// <param name="query">The OData options</param>
    public virtual OrderByQueryOption GetOdataOrderByQueryOption(ODataQueryOptions<TEntity> query)
    {
        var context = query.Context;
        var oDataQueryOptionParser = new ODataQueryOptionParser(
            context.Model,
            context.ElementType,
            context.NavigationSource,
            new Dictionary<string, string> { { "$orderby", query.OrderBy.RawValue } })
        {
            Resolver = CaseInsensitiveResolver
        };
        return new OrderByQueryOption(query.OrderBy.RawValue, context, oDataQueryOptionParser);
    }

    private static ODataUriResolver CaseInsensitiveResolver => new() { EnableCaseInsensitive = true };

    /// <summary>
    /// Execute custom query that extract from odata $filter
    /// </summary>
    /// <param name="queryable">The query so far</param>
    /// <param name="customQuery">Custom functions found in the filter</param>
    protected virtual IQueryable<TEntity> GetQueryable(IQueryable<TEntity> queryable,
        IList<CustomOdataFiler> customQuery)
    {
        var gQuery = customQuery.GroupBy(g => g.FunctionName.ToLowerInvariant());
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
                        out var parameters));
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
    protected virtual (string Alias, string SelectQuery) CustomQuerySelectQuery() => ("", "");

    /// <summary>
    /// Join Sql Table query mapped to Property in OData
    /// </summary>
    /// <param name="navProperty">Navigation property name</param>
    protected virtual (string Alias, string JoinQuery) CustomQueryJoinQuery(string navProperty)
    {
        string alias = "", joinQuery = "";

        return (alias, joinQuery);
    }

    /// <summary>
    /// Populate Odata filter to extract custom functions. A custom function under <c>or</c> or <c>not</c> is rejected with 400.
    /// </summary>
    /// <param name="query">The OData options</param>
    protected virtual (FilterQueryOption? Filter, List<CustomOdataFiler>? CustomFilter) PopulateFilter(
        ODataQueryOptions<TEntity> query)
    {
        if (query.Filter == null)
        {
            return (null, null);
        }

        // standard filters go to OData untouched: only a filter that names a custom function is taken apart
        if (!ODataFilterTokenizer.ContainsCustomFunction(query.Filter.RawValue, CustomFunctions))
        {
            return (query.Filter, null);
        }

        var split = ODataFilterTokenizer.Split(query.Filter.RawValue, CustomFunctions);
        if (split.CustomFilters.Count == 0)
        {
            return (query.Filter, null);
        }

        FilterQueryOption? odataFilter = null;
        if (!string.IsNullOrWhiteSpace(split.Filter))
        {
            var queryOptions = new Dictionary<string, string> { { "$filter", split.Filter } };
            var parser = new ODataQueryOptionParser(query.Context.Model, query.Context.ElementType,
                query.Context.NavigationSource, queryOptions)
            {
                Resolver = CaseInsensitiveResolver
            };
            odataFilter = new FilterQueryOption(split.Filter, query.Context, parser);
        }

        return (odataFilter, split.CustomFilters.ToList());
    }

    /// <summary>
    /// Builds the SQL for <c>xmlExist(path, 'value')</c> filters. Nothing the caller sent is pasted into the SQL text:
    /// every path segment must be a mapped property of the entity (walked through the CLR model) and the value must be
    /// exactly one quoted literal, which travels as the SQL parameter <c>@pN</c> (N is its index in <paramref name="parameters"/>).
    /// The element text of the xml column is compared with the value: <c>col.exist('//*[text() = sql:variable("@pN")]') = 1</c>.
    /// </summary>
    /// <param name="alias">Table alias</param>
    /// <param name="list">The calls</param>
    /// <param name="parameters">Sql parameter values; the value at index N is <c>@pN</c></param>
    /// <exception cref="ApiException">A path or value is not acceptable (400)</exception>
    protected virtual string QueryXmlExist(string alias, List<CustomOdataFiler> list, out List<object> parameters)
    {
        var values = new List<object>();
        parameters = values;

        var whereBuilder = new StringBuilder();
        var joinBuilder = new StringBuilder();
        var addedTables = new List<string>();

        foreach (var call in list)
        {
            if (call.Parameters.Count != 2)
            {
                throw InvalidFilter("xmlExist needs exactly two arguments: a property path and one quoted value.");
            }

            var navProperties = ValidatePropertyPath(call.Parameters[0]);
            var value = ParseSingleLiteral(call.Parameters[1]);

            var currentAlias = alias;
            for (var navIndex = 0; navIndex < navProperties.Length - 1; navIndex++)
            {
                var joinProp = CustomQueryJoinQuery(navProperties[navIndex]);
                if (!addedTables.Contains(navProperties[navIndex]))
                {
                    joinBuilder.AppendLine(joinProp.JoinQuery);
                    addedTables.Add(navProperties[navIndex]);
                }

                currentAlias = joinProp.Alias;
            }

            var parameterName = "@p" + values.Count;
            values.Add(value);
            if (whereBuilder.Length > 0)
            {
                whereBuilder.Append(" and ");
            }

            whereBuilder.Append($"{currentAlias}.{navProperties[^1]}.exist('//*[text() = sql:variable(\"{parameterName}\")]') = 1");
        }

        return $"{joinBuilder} WHERE {whereBuilder}";
    }

    private static ApiException InvalidFilter(string message) =>
        new($"Invalid $filter. {message}", statusCode: System.Net.HttpStatusCode.BadRequest, code: "InvalidFilter");

    /// <summary>The value must be one quoted literal and nothing else; quotes inside are doubled.</summary>
    private static string ParseSingleLiteral(string argument)
    {
        const int maxLength = 1000;
        var text = argument.Trim();
        if (text.Length < 2 || (text[0] != '\'' && text[0] != '"') || text[^1] != text[0])
        {
            throw InvalidFilter("The value must be a single quoted literal.");
        }

        var quote = text[0];
        var body = text[1..^1];
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != quote)
            {
                continue;
            }

            // a doubled quote is an escaped quote; a lone quote would end the literal early
            if (i + 1 < body.Length && body[i + 1] == quote)
            {
                i++;
                continue;
            }

            throw InvalidFilter("The value must be a single quoted literal.");
        }

        var value = body.Replace(new string(quote, 2), quote.ToString());
        if (value.Length > maxLength)
        {
            throw InvalidFilter("The value is too long.");
        }

        return value;
    }

    /// <summary>
    /// Splits <c>a/b/c</c> and checks that each segment is a plain identifier and a public property of the type reached so far
    /// (collection properties continue with their element type).
    /// </summary>
    private static string[] ValidatePropertyPath(string path)
    {
        var segments = path.Trim().Split('/');
        var type = typeof(TEntity);
        var result = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (!IdentifierPattern().IsMatch(segment))
            {
                throw InvalidFilter($"'{(segment.Length > 40 ? segment[..40] : segment)}' is not a valid property name.");
            }

            var property = type.GetProperty(segment, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property == null)
            {
                throw InvalidFilter($"'{segment}' is not a property of {type.Name}.");
            }

            result[i] = property.Name;
            type = property.PropertyType;
            if (type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
            {
                type = type.GetGenericArguments()[0];
            }
        }

        return result;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial System.Text.RegularExpressions.Regex IdentifierPattern();

    #endregion

    /// <summary>
    /// Get Queryable object
    /// </summary>
    /// <param name="query">The OData options</param>
    /// <param name="pageSize">Resulting page size</param>
    /// <param name="skip">Resulting skip</param>
    /// <param name="pageNumber">Resulting 1-based page number</param>
    /// <param name="includeProperties">Properties to include</param>
    /// <param name="fixCriteria">Scope criteria always applied</param>
    /// <param name="flags">Query flags (untracked unless <see cref="QueryFlags.EnableTracking"/> is given)</param>
    public virtual IQueryable<TEntity> GetQueryable(ODataQueryOptions<TEntity> query, out int pageSize, out int skip,
        out int pageNumber, string[]? includeProperties = null, Expression<Func<TEntity, bool>>? fixCriteria = null, QueryFlags? flags = null)
    {
        ODataQuerySettings odataSettings;
        (pageSize, skip, pageNumber, odataSettings) = GetOdataProperties(query);

        //Get the expanded properties
        var extendProperties = GetOdataPropertiesExpandProperties(query, includeProperties);
        // OData reads are untracked; the repository lets QueryFlags.EnableTracking win
        var queryFlags = (flags ?? 0) | QueryFlags.DisableTracking;
        var queryableEntity = CurrentRepository.GetQueryable(extendProperties, queryFlags);
        var populatedQuery = PopulateFilter(query);
        if (populatedQuery.CustomFilter != null)
        {
            queryableEntity = GetQueryable(queryableEntity, populatedQuery.CustomFilter);
        }

        //Apply filter
        if (populatedQuery.Filter != null)
            queryableEntity = (IQueryable<TEntity>)populatedQuery.Filter.ApplyTo(queryableEntity, odataSettings);

        //Apply Order By
        if (query.OrderBy != null)
        {
            var orderBy = GetOdataOrderByQueryOption(query);
            queryableEntity = (IQueryable<TEntity>)orderBy.ApplyTo(queryableEntity, odataSettings);
        }
        else
        {
            // Skip/Take without an order is not deterministic (and EF warns); fall back to the primary key
            queryableEntity = CurrentRepository.ApplyKeyOrder(queryableEntity);
        }

        if (fixCriteria != null)
        {
            queryableEntity = queryableEntity.Where(fixCriteria);
        }

        return queryableEntity;
    }

    #endregion

    /// <summary>
    /// Check if entity is exist
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<bool> Exists(TKey id, CancellationToken cancellationToken = default)
    {
        return await CurrentRepository.AnyAsync(ResolveKeyPredicate(id), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// If need to add additional property's value or calculated properties without using view model
    /// </summary>
    /// <param name="entity">The entity</param>
    public virtual TEntity? PopulateAdditionalProperties(TEntity? entity)
    {
        return entity;
    }

    /// <summary>Builds a data table from the view models of an OData query.</summary>
    public virtual async Task<DataTable> GetViewModelsDataTable<TResult>(
        ODataQueryOptions<TEntity> query,
        Func<TEntity, TResult> mapper,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        string[]? includeProperties = null,
        IList<DataColumn>? columns = null,
        Func<TResult, IList<DataColumn>, DataRow>? getDataRow = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        var dbResult = new DataTable(typeof(TEntity).Name);
        var result = await GetCustomViewModels(mapper, query, fixCriteria, includeProperties,
            cancellationToken);
        columns ??= GetColumns();
        // a row can only be added to the table it was created from, so the table needs the columns first
        foreach (var column in columns)
        {
            if (!dbResult.Columns.Contains(column.ColumnName))
            {
                dbResult.Columns.Add(column.ColumnName, column.DataType);
            }
        }

        if (getDataRow == null) return dbResult;
        foreach (var item in result.Items)
        {
            var row = getDataRow(item, columns);
            if (ReferenceEquals(row.Table, dbResult))
            {
                dbResult.Rows.Add(row);
            }
            else
            {
                dbResult.LoadDataRow(row.ItemArray, true);
            }
        }

        return dbResult;
    }

    /// <summary>Columns of the data table; none by default.</summary>
    protected virtual IList<DataColumn> GetColumns()
    {
        return new List<DataColumn>();
    }

    /// <summary>
    /// Export data to file format based on oData query. Not implemented here; override it.
    /// </summary>
    /// <exception cref="NotImplementedException">Always, until overridden</exception>
    public virtual Task<Stream> ExportFile<TResult>(ExportConfiguration config, object oDataQueryOptions,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        string[]? includeProperties = null, CancellationToken cancellationToken = default)
        where TResult : class
    {
        throw new NotImplementedException($"{typeof(TEntity).Name} export files is not implemented");
    }

    /// <summary>
    /// Create the select function for an OData <c>$select</c>; prefer <see cref="CreateSelectExpression"/>, which the database can translate.
    /// </summary>
    /// <param name="fields">Comma separated property names</param>
    protected virtual Func<TEntity, TEntity> CreateNewStatement(string fields)
    {
        return CreateSelectExpression(fields).Compile();
    }

    /// <summary>
    /// Create <c>e =&gt; new TEntity { A = e.A, B = e.B }</c> for an OData <c>$select</c>. Property names match case-insensitively.
    /// </summary>
    /// <param name="fields">Comma separated property names</param>
    /// <exception cref="ArgumentException">A name is not a settable property of the entity</exception>
    protected virtual Expression<Func<TEntity, TEntity>> CreateSelectExpression(string fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var xParameter = Expression.Parameter(typeof(TEntity), "e");
        var xNew = Expression.New(typeof(TEntity));

        var bindings = fields.Split(',').Select(e => e.Trim()).Where(e => e.Length > 0)
            .Select(e =>
            {
                var mi = typeof(TEntity).GetProperty(e, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (mi == null || !mi.CanWrite)
                {
                    throw new ArgumentException($"'{e}' is not a selectable property of {typeof(TEntity).Name}.", nameof(fields));
                }

                return Expression.Bind(mi, Expression.Property(xParameter, mi));
            })
            .ToList();

        return Expression.Lambda<Func<TEntity, TEntity>>(Expression.MemberInit(xNew, bindings), xParameter);
    }
}
