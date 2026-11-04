using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Data;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services;

namespace TabTabGo.Core.TestKit;

/// <summary>Product service over the repository of the unit of work.</summary>
public class TestProductService : BaseService<Product, int>
{
    public TestProductService(IUnitOfWork unitOfWork, ILogger<BaseService<Product, int>> logger) : base(unitOfWork, logger)
    {
    }

    /// <summary>Messages recorded by the hooks, in call order.</summary>
    public List<string> Calls { get; } = new();

    public Func<Product, string?> DeleteRule { get; set; } = _ => null;

    public Func<Product, string?> CreateRule { get; set; } = _ => null;

    public Func<Product, string?> UpdateRule { get; set; } = _ => null;

    protected override string[] IncludeProperties => new[] { nameof(Product.Category) };

    protected override string[] IgnoredProperties { get; } = new[] { nameof(Product.Category) };

    public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

    public override int GetKey(Product entity) => entity.Id;

    public override bool IsValidToCreate(Product entity, out string validationErrorMessage)
    {
        validationErrorMessage = CreateRule(entity) ?? string.Empty;
        return validationErrorMessage.Length == 0;
    }

    public override bool IsValidToUpdate(Product? entity, out string validationErrorMessage)
    {
        validationErrorMessage = entity == null ? string.Empty : UpdateRule(entity) ?? string.Empty;
        return validationErrorMessage.Length == 0;
    }

    public override bool IsValidToDelete(Product entity, out string validationErrorMessage)
    {
        validationErrorMessage = DeleteRule(entity) ?? string.Empty;
        return validationErrorMessage.Length == 0;
    }

    protected override Task PreCreate(Product entity, CancellationToken cancellationToken = default)
    {
        Calls.Add("PreCreate");
        return Task.CompletedTask;
    }

    protected override Task PostCreate(Product entity, CancellationToken cancellationToken = default)
    {
        Calls.Add("PostCreate");
        return Task.CompletedTask;
    }

    public override Task PreUpdate(Product current, Product entity, CancellationToken cancellationToken = default)
    {
        Calls.Add("PreUpdate");
        return Task.CompletedTask;
    }

    public override Task PostUpdate(Product entity, CancellationToken cancellationToken = default)
    {
        Calls.Add("PostUpdate");
        return Task.CompletedTask;
    }

    protected override Task PreDelete(Product entity, CancellationToken cancellationToken = default)
    {
        Calls.Add("PreDelete");
        return Task.CompletedTask;
    }

    protected override Task PostDelete(Product entity, CancellationToken cancellationToken = default)
    {
        Calls.Add("PostDelete");
        return Task.CompletedTask;
    }

    /// <summary>The loaded entity, for assertions.</summary>
    public Product? Loaded => CurrentEntity;
}

/// <summary>Hand-written mapper (no mocking library).</summary>
public class FakeProductMapper : IMapper<Product, ProductViewModel>
{
    public ProductViewModel MapToViewModel(Product entity) => new() { Id = entity.Id, Name = entity.Name, Price = entity.Price };

    public Product MapToEntity(ProductViewModel viewModel) => new() { Id = viewModel.Id, Name = viewModel.Name, Price = viewModel.Price };

    public Product MapFromRequest<TRequest>(TRequest viewModel) where TRequest : class =>
        viewModel is ProductCreateRequest request
            ? new Product { Name = request.Name, Price = request.Price, Tenant = request.Tenant }
            : throw new ArgumentException("Unsupported request", nameof(viewModel));

    public void PopulateViewModel(Product entity, ProductViewModel viewModel)
    {
        viewModel.Id = entity.Id;
        viewModel.Name = entity.Name;
        viewModel.Price = entity.Price;
    }

    public PageList<ProductViewModel> MapPaging(PageList<Product> entitiesResult) =>
        new(MapList(entitiesResult.Items).ToList(), entitiesResult.TotalItems, entitiesResult.PageSize, entitiesResult.PageNumber);

    public IEnumerable<ProductViewModel> MapList(IEnumerable<Product> entitiesResult) => entitiesResult.Select(MapToViewModel);
}

/// <summary>Validation service that rejects products with an empty name or the name "invalid".</summary>
public class FakeProductValidator : IValidationService<Product, ProductCreateRequest>
{
    public Task<ValidationResult> Validate(Product entity, CancellationToken cancellationToken) =>
        Task.FromResult(Check(entity.Name));

    public Task<ValidationResult> Validate(ProductCreateRequest entityRequest, CancellationToken cancellationToken) =>
        Task.FromResult(Check(entityRequest.Name));

    private static ValidationResult Check(string name)
    {
        var result = new ValidationResult { IsValid = !string.IsNullOrWhiteSpace(name) && name != "invalid" };
        if (!result.IsValid)
        {
            result.Errors["Name"] = new List<string> { "Name is not valid" };
        }

        return result;
    }
}

/// <summary>Repository that implements only the members 8.x implementers had (the default interface methods fill the rest).</summary>
public class MinimalReadRepository : IGenericReadRepository<Product, int>
{
    public List<Product> Data { get; } = new();

    public Task<PageList<TResult>> GetPageListAsync<TResult>(Func<Product, TResult> selector, Expression<Func<Product, bool>>? filter = null, int pageNumber = 0, int pageSize = 20,
        Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, TabTabGo.Core.Enums.QueryFlags? flags = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        var query = filter == null ? Data.AsQueryable() : Data.AsQueryable().Where(filter);
        var items = query.Skip(pageNumber * pageSize).Take(pageSize).Select(selector).ToList();
        return Task.FromResult(new PageList<TResult>(items, query.Count(), pageSize, pageNumber + 1));
    }

    public IQueryable<TResult> GetQueryable<TResult>(Expression<Func<Product, TResult>>? selector = null, Expression<Func<Product, bool>>? filter = null,
        Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null,
        TabTabGo.Core.Enums.QueryFlags? flags = null) where TResult : class => throw new NotSupportedException();

    public IEnumerable<Product> Get(Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null,
        string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null) =>
        filter == null ? Data : Data.AsQueryable().Where(filter).ToList();

    public Task<IEnumerable<Product>> GetAsync(Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null,
        string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null,
        CancellationToken cancellationToken = default) => Task.FromResult(Get(filter));

    public IEnumerable<TResult> Get<TResult>(Expression<Func<Product, TResult>> selector, Expression<Func<Product, bool>>? filter = null,
        Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null,
        TabTabGo.Core.Enums.QueryFlags? flags = null) where TResult : class => throw new NotSupportedException();

    public Task<IEnumerable<TResult>> GetAsync<TResult>(Expression<Func<Product, TResult>> selector, Expression<Func<Product, bool>>? filter = null,
        Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToTake = null, int? rowsToSkip = null,
        TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) where TResult : class => throw new NotSupportedException();

    public Product? GetByKey(int key, string[]? includeProperties = null) => Data.FirstOrDefault(p => p.Id == key);

    public Task<Product?> GetByKeyAsync(int key, string[]? includeProperties = null, CancellationToken cancellationToken = default) => Task.FromResult(GetByKey(key));

    public int Count(Expression<Func<Product, bool>>? filter = null, string[]? includes = null, TabTabGo.Core.Enums.QueryFlags? flags = null) =>
        filter == null ? Data.Count : Data.AsQueryable().Count(filter);

    public Task<int> CountAsync(Expression<Func<Product, bool>>? filter = null, string[]? includes = null, TabTabGo.Core.Enums.QueryFlags? flags = null,
        CancellationToken cancellationToken = default) => Task.FromResult(Count(filter));

    public Product? FirstOrDefault(Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null,
        string[]? includeProperties = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null) => Get(filter).FirstOrDefault();

    public Task<Product?> FirstOrDefaultAsync(Expression<Func<Product, bool>>? filter = null, Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null,
        string[]? includeProperties = null, int? rowsToSkip = null, TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(FirstOrDefault(filter));

    public TResult? FirstOrDefault<TResult>(Expression<Func<Product, TResult>> selector, Expression<Func<Product, bool>>? filter = null,
        Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToSkip = null,
        TabTabGo.Core.Enums.QueryFlags? flags = null) where TResult : class => throw new NotSupportedException();

    public Task<TResult?> FirstOrDefaultAsync<TResult>(Expression<Func<Product, TResult>> selector, Expression<Func<Product, bool>>? filter = null,
        Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy = null, string[]? includeProperties = null, int? rowsToSkip = null,
        TabTabGo.Core.Enums.QueryFlags? flags = null, CancellationToken cancellationToken = default) where TResult : class => throw new NotSupportedException();

    public IQueryable<Product> GetQueryable(TabTabGo.Core.Enums.QueryFlags? flags = null) => Data.AsQueryable();

    public IQueryable<Product> GetQueryable(string[] includeProperties, TabTabGo.Core.Enums.QueryFlags? flags = null) => Data.AsQueryable();

    public Task<int> ExecuteSqlCommand(string query, IDictionary<string, object>? parameters = null, CancellationToken cancellationToken = default) => Task.FromResult(0);

    public IQueryable<Product> SqlQuery(string query, System.Data.CommandType cmdType, params object[] parameters) => Data.AsQueryable();

    public IQueryable<Product> SqlQuery(IQueryable<Product> queryable, string query, System.Data.CommandType cmdType, params object[] parameters) => queryable;

    public IQueryable<Product> IncludeProperties(IQueryable<Product> queryable, string[] includeProperties) => queryable;

    public IQueryable<Product> SetFlags(IQueryable<Product> queryable, TabTabGo.Core.Enums.QueryFlags? flags) => queryable;

    public void Dispose()
    {
    }
}

/// <summary>Unit of work that implements only the 8.x members (the default interface methods fill the rest).</summary>
public class MinimalUnitOfWork : IUnitOfWork
{
    public List<string> Calls { get; } = new();

    public IGenericRepository<TEntity, TKey> Repository<TEntity, TKey>(string? name = null) where TEntity : class => throw new NotSupportedException();

    public void BeginTransaction(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.ReadCommitted) => Calls.Add($"Begin:{isolationLevel}");

    public void Commit() => Calls.Add("Commit");

    public void Rollback() => Calls.Add("Rollback");

    public int SaveChanges() => 0;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<int> SaveChangesInBulkAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public void AddOrUpdateGraph<TEntity>(TEntity entity) where TEntity : class
    {
    }

    public void SetEntityStateModified<TEntity, TProperty>(TEntity entity, Expression<Func<TEntity, TProperty>> propertyExpression) where TEntity : class where TProperty : class
    {
    }

    public void RemoveNavigationProperty<TEntity, TOwnerEntity>(TOwnerEntity ownerEntity, object id) where TEntity : class where TOwnerEntity : class
    {
    }

    public IReadOnlyList<EntityChange> GetChanges() => Array.Empty<EntityChange>();

    public void Dispose()
    {
    }
}
