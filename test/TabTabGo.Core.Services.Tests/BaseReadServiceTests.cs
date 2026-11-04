using TabTabGo.Core.Data;
using System.Linq.Expressions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.TestKit;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Services.Tests;

public class BaseReadServiceTests : ServiceTestBase
{
    // --- plain reads -------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetList_FilterApplied_ReturnsMatchingWithDefaultIncludes()
    {
        var list = (await Service.GetList(p => p.Price > 200, cancellationToken: Ct)).ToList();

        Assert.Equal(5, list.Count);
        Assert.All(list, p => Assert.NotNull(p.Category));
    }

    [Fact]
    public async Task GetPageList_ZeroBasedPage_ReturnsOneBasedPageNumber()
    {
        var page = await Service.GetPageList(p => true, page: 1, pageSize: 10, cancellationToken: Ct);

        Assert.Equal(2, page.PageNumber);
        Assert.Equal(10, page.Count);
        Assert.Equal(25, page.TotalItems);
        Assert.Equal("Product 11", page.Items[0].Name);
    }

    [Fact]
    public async Task GetPageList_WithOrderBy_UsesIt()
    {
        var page = await Service.GetPageList(p => true, q => q.OrderByDescending(p => p.Price), pageSize: 3, cancellationToken: Ct);

        Assert.Equal(new[] { "Product 25", "Product 24", "Product 23" }, page.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task GetByKey_Existing_ReturnsEntityAndSetsCurrent()
    {
        var product = await Service.GetByKey(3, cancellationToken: Ct);

        Assert.NotNull(product);
        Assert.Equal("Product 03", product.Name);
        Assert.Same(product, Service.Loaded);
    }

    [Fact]
    public async Task GetByKey_Missing_ReturnsNull()
    {
        Assert.Null(await Service.GetByKey(999, cancellationToken: Ct));
    }

    [Fact]
    public async Task GetByKey_FixCriteriaOutsideScope_ReturnsNull()
    {
        Expression<Func<Product, bool>> scope = p => p.Tenant == "A";

        Assert.Null(await Service.GetByKey(1, fixCriteria: scope, cancellationToken: Ct)); // odd ids are tenant B
        Assert.NotNull(await Service.GetByKey(2, fixCriteria: scope, cancellationToken: Ct));
    }

    [Fact]
    public async Task GetByKey_LastUpdatedDate_OnlyEntitiesUpdatedBeforeAreReturned()
    {
        // documents the current rule: lastUpdatedDate > entity.UpdatedDate
        Assert.NotNull(await Service.GetByKey(1, DateTimeOffset.UtcNow.AddDays(1), cancellationToken: Ct));
        Assert.Null(await Service.GetByKey(1, DateTimeOffset.MinValue, cancellationToken: Ct));
    }

    [Fact]
    public async Task GetViewModelByKey_MapsEntity()
    {
        var vm = await Service.GetViewModelByKey(2, p => new ProductViewModel { Id = p.Id, Name = p.Name }, cancellationToken: Ct);

        Assert.Equal("Product 02", vm?.Name);
        Assert.Null(await Service.GetViewModelByKey(999, p => new ProductViewModel(), cancellationToken: Ct));
    }

    [Fact]
    public async Task Exists_UsesAsyncAny()
    {
        Assert.True(await Service.Exists(5, Ct));
        Assert.False(await Service.Exists(500, Ct));
    }

    // --- OData -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPageList_ODataFilterAndTop_ReturnsPage()
    {
        var options = ODataTestHelper.Create("$filter=Price gt 100&$top=5&$orderby=Price");

        var page = await Service.GetPageList(options, cancellationToken: Ct);

        Assert.Equal(15, page.TotalItems);
        Assert.Equal(5, page.Count);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("Product 11", page.Items[0].Name);
    }

    [Fact]
    public async Task GetPageList_ODataSkip_PageNumberIsOneBased()
    {
        var options = ODataTestHelper.Create("$top=10&$skip=20&$orderby=Id");

        var page = await Service.GetPageList(options, cancellationToken: Ct);

        Assert.Equal(3, page.PageNumber);
        Assert.Equal(5, page.Count);
    }

    [Fact]
    public async Task GetPageList_ODataFixCriteria_IsAlwaysApplied()
    {
        var options = ODataTestHelper.Create("$orderby=Id");

        var page = await Service.GetPageList(options, p => p.Tenant == "A", cancellationToken: Ct);

        Assert.Equal(12, page.TotalItems);
        Assert.All(page.Items, p => Assert.Equal("A", p.Tenant));
    }

    [Fact]
    public async Task GetPageList_ODataWrongOptionsObject_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service.GetPageList(new object(), cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service.GetPageList((object)null!, cancellationToken: Ct));
    }

    [Fact]
    public async Task GetPageList_ODataSelect_IsTranslatedToSql_NotFilteredInMemory()
    {
        var options = ODataTestHelper.Create("$select=Name&$top=3&$orderby=Id");

        var page = await Service.GetPageList(options, cancellationToken: Ct);

        Assert.Equal(3, page.Count);
        Assert.All(page.Items, p =>
        {
            Assert.NotEqual(string.Empty, p.Name);
            Assert.Equal(0m, p.Price); // not selected, so not read
        });
    }

    [Fact]
    public void CreateSelectExpression_UnknownProperty_Throws()
    {
        var service = new ExposedService(UnitOfWork);

        Assert.Throws<ArgumentException>(() => service.Select("Name,Nope"));
        Assert.Contains("Name", service.Select("name").ToString());
    }

    [Fact]
    public void CreateNewStatement_ReturnsCompiledSelector()
    {
        var service = new ExposedService(UnitOfWork);

        var func = service.NewStatement("Name");

        Assert.Equal("x", func(new Product { Name = "x", Price = 5 }).Name);
    }

    [Fact]
    public async Task GetPageList_ODataOrderByCaseInsensitive_Works()
    {
        var options = ODataTestHelper.Create("$orderby=price desc&$top=2");

        var page = await Service.GetPageList(options, cancellationToken: Ct);

        Assert.Equal("Product 25", page.Items[0].Name);
    }

    [Fact]
    public async Task GetViewModels_FlagsDisableTracking_EntitiesNotTracked()
    {
        var options = ODataTestHelper.Create("$top=3&$orderby=Id");

        var page = await Service.GetViewModels<ProductViewModel>(options, p => new ProductViewModel { Id = p.Id }, cancellationToken: Ct);

        Assert.Equal(3, page.Count);
        Assert.Empty(Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetViewModelsObjectMapper_ReturnsPage()
    {
        // used to recurse until the stack overflowed (N-1)
        var options = ODataTestHelper.Create("$top=4&$orderby=Id");

        var page = await Service.GetViewModels<ProductViewModel>((object)options, p => new ProductViewModel { Id = p.Id }, cancellationToken: Ct);

        Assert.Equal(4, page.Count);
        Assert.Equal(1, page.PageNumber);
    }

    [Fact]
    public async Task GetViewModelsObject_DefaultMapper_ReturnsEntities()
    {
        var options = ODataTestHelper.Create("$top=2&$orderby=Id");

        var page = await Service.GetViewModels(options, cancellationToken: Ct);

        Assert.Equal(2, page.Count);
        Assert.IsType<Product>(page.Items[0]);
    }

    [Fact]
    public async Task GetViewModelsGenericNoMapper_AssignableType_ReturnsEntities()
    {
        var options = ODataTestHelper.Create("$top=2&$orderby=Id");

        var page = await Service.GetViewModels<Product>(options, cancellationToken: Ct);
        Assert.Equal(2, page.Count);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.GetViewModels<ProductViewModel>(options, cancellationToken: Ct));
    }

    [Fact]
    public async Task GetCustomViewModels_RoutesToMapperOverload()
    {
        var options = ODataTestHelper.Create("$top=2&$orderby=Id");

        var page = await Service.GetCustomViewModels(p => new ProductViewModel { Id = p.Id }, options, cancellationToken: Ct);

        Assert.Equal(new[] { 1, 2 }, page.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task GetViewModelsDataTable_BuildsRows()
    {
        var options = ODataTestHelper.Create("$top=3&$orderby=Id");
        var column = new System.Data.DataColumn("Id", typeof(int));

        var table = await Service.GetViewModelsDataTable(options, p => new ProductViewModel { Id = p.Id }, columns: new[] { column },
            getDataRow: (vm, cols) =>
            {
                var t = new System.Data.DataTable();
                t.Columns.Add("Id", typeof(int));
                var row = t.NewRow();
                row["Id"] = vm.Id;
                return row;
            }, cancellationToken: Ct);

        Assert.Equal(3, table.Rows.Count);
    }

    [Fact]
    public async Task GetViewModelsDataTable_NoRowFactory_ReturnsEmptyTable()
    {
        var options = ODataTestHelper.Create("$top=3");

        var table = await Service.GetViewModelsDataTable(options, p => new ProductViewModel(), cancellationToken: Ct);

        Assert.Empty(table.Rows);
        Assert.Equal(nameof(Product), table.TableName);
    }

    [Fact]
    public async Task ExportFile_NotOverridden_ThrowsNotImplementedWithEntityName()
    {
        var ex = await Assert.ThrowsAsync<NotImplementedException>(() =>
            Service.ExportFile<object>(new ExportConfiguration(), ODataTestHelper.Create(""), cancellationToken: Ct));

        Assert.Contains(nameof(Product), ex.Message);
        Assert.DoesNotContain("TEntity", ex.Message);
    }

    [Fact]
    public void GetOdataProperties_NoTop_UsesDefaults()
    {
        var service = new ExposedService(UnitOfWork);

        var (pageSize, skip, pageNumber, settings) = service.GetOdataProperties(ODataTestHelper.Create(""));
        var nullCase = service.GetOdataProperties(null);

        Assert.Equal(50, pageSize);
        Assert.Equal(0, skip);
        Assert.Equal(1, pageNumber);
        Assert.Equal(50, settings.PageSize);
        Assert.Equal(50, nullCase.pageSize);
    }

    [Fact]
    public void GetOdataProperties_ZeroOrNegativeTop_FallsBackToDefault()
    {
        var service = new ExposedService(UnitOfWork);

        Assert.Equal(50, service.GetOdataProperties(ODataTestHelper.Create("$top=0")).pageSize);
        Assert.Equal(7, service.GetOdataProperties(ODataTestHelper.Create("$top=7&$skip=14")).pageSize);
        Assert.Equal(3, service.GetOdataProperties(ODataTestHelper.Create("$top=7&$skip=14")).pageNumber);
    }

    [Fact]
    public void GetOdataPropertiesExpandProperties_MergesDefaultsGivenAndExpand()
    {
        var service = new ExposedService(UnitOfWork);

        var result = service.GetOdataPropertiesExpandProperties(ODataTestHelper.Create("$expand=Lines"), new[] { "Extra", "$skipped" });

        Assert.Equal(new[] { "Category", "Extra", "Lines" }, result.OrderBy(x => x));
    }

    [Fact]
    public void GetQueryable_OData_ReturnsComposableQuery()
    {
        var query = Service.GetQueryable(ODataTestHelper.Create("$filter=Price le 30&$orderby=Price"), out var pageSize, out var skip, out var pageNumber);

        Assert.Equal(3, query.Count());
        Assert.Equal((50, 0, 1), (pageSize, skip, pageNumber));
    }

    [Fact]
    public void GetQueryable_NullFlags_StillDisablesTracking()
    {
        // DisableTracking | null used to evaluate to null, which turned tracking on (N-5)
        var query = Service.GetQueryable(ODataTestHelper.Create(""), out _, out _, out _, flags: null);

        _ = query.ToList();

        Assert.Empty(Context.ChangeTracker.Entries());
    }

    // --- custom filter functions ---------------------------------------------------------------------------------

    [Fact]
    public void PopulateFilter_OnlyStandardFunctions_KeepsFilterAsIs()
    {
        var service = new ExposedService(UnitOfWork);

        var (filter, custom) = service.Populate(ODataTestHelper.Create("$filter=Price gt 10 and contains(Name,'a')"));

        Assert.NotNull(filter);
        Assert.Null(custom);
    }

    [Theory]
    [InlineData("(Price mul Id) gt 100")]
    [InlineData("((Price add 1) mul (Id sub 2)) gt 100")]
    [InlineData("contains(Name,'xml') and (Price add 1) gt 2")] // 'xml' in a literal is not the function
    public void PopulateFilter_StandardFilterWithGroupedOperands_PassesThroughUntouched(string filter)
    {
        var service = new ExposedService(UnitOfWork);
        var options = ODataTestHelper.Create("$filter=" + filter);

        var before = TabTabGo.Core.Services.OData.ODataFilterTokenizer.Calls;

        var (populated, custom) = service.Populate(options);

        Assert.Equal(before, TabTabGo.Core.Services.OData.ODataFilterTokenizer.Calls); // the tokenizer never saw it
        Assert.Same(options.Filter, populated);
        Assert.Null(custom);
    }

    [Fact]
    public async Task GetPageList_ODataGroupedArithmeticFilter_Works()
    {
        var page = await Service.GetPageList(ODataTestHelper.Create("$filter=(Price mul Id) gt 3000&$orderby=Id"), cancellationToken: Ct);

        Assert.True(page.Count > 0);
        Assert.All(page.Items, p => Assert.True(p.Price * p.Id > 3000));
    }

    [Fact]
    public void PopulateFilter_NoFilter_ReturnsNothing()
    {
        var service = new ExposedService(UnitOfWork);

        var (filter, custom) = service.Populate(ODataTestHelper.Create(""));

        Assert.Null(filter);
        Assert.Null(custom);
    }

    [Fact]
    public void PopulateFilter_CustomFunctionWithAnd_IsExtractedAndRestKept()
    {
        var service = new ExposedService(UnitOfWork);

        var (filter, custom) = service.Populate(ODataTestHelper.Create("$filter=Price gt 10 and xmlExist(Description,'abc')"));

        Assert.NotNull(filter);
        Assert.Equal("Price gt 10", filter!.RawValue);
        var call = Assert.Single(custom!);
        Assert.Equal("xmlExist", call.FunctionName);
        Assert.Equal(new[] { "Description", "'abc'" }, call.Parameters);
    }

    [Fact]
    public void PopulateFilter_OnlyCustomFunction_LeavesNoFilter()
    {
        var service = new ExposedService(UnitOfWork);

        var (filter, custom) = service.Populate(ODataTestHelper.Create("$filter=xmlExist(Description,'abc')"));

        Assert.Null(filter);
        Assert.Single(custom!);
    }

    [Fact]
    public void PopulateFilter_CustomFunctionUnderOr_Is400()
    {
        var service = new ExposedService(UnitOfWork);

        var ex = Assert.Throws<ApiException>(() => service.Populate(ODataTestHelper.Create("$filter=Price gt 10 or xmlExist(Description,'abc')")));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.HttpStatusCode);
    }

    // --- xmlExist SQL: injection (D-35) --------------------------------------------------------------------------

    [Fact]
    public void QueryXmlExist_ValidCall_ParameterisesTheValue()
    {
        var service = new ExposedService(UnitOfWork);
        var call = new CustomOdataFiler { FunctionName = "xmlExist", Parameters = { "Description", "'hello'" } };

        var sql = service.XmlExist("t", new List<CustomOdataFiler> { call }, out var parameters);

        Assert.Contains("t.Description.exist('//*[text() = sql:variable(\"@p0\")]') = 1", sql);
        Assert.DoesNotContain("hello", sql);
        Assert.Equal(new object[] { "hello" }, parameters);
    }

    [Fact]
    public void QueryXmlExist_SeveralCalls_GetOwnParametersAndAreAnded()
    {
        var service = new ExposedService(UnitOfWork);
        var calls = new List<CustomOdataFiler>
        {
            new() { FunctionName = "xmlExist", Parameters = { "Description", "'a'" } },
            new() { FunctionName = "xmlExist", Parameters = { "Name", "\"b\"" } },
        };

        var sql = service.XmlExist("t", calls, out var parameters);

        Assert.Contains("@p0", sql);
        Assert.Contains("@p1", sql);
        Assert.Contains(" and ", sql);
        Assert.Equal(new object[] { "a", "b" }, parameters);
    }

    [Fact]
    public void QueryXmlExist_EscapedQuoteInsideLiteral_IsUnescapedIntoTheParameter()
    {
        var service = new ExposedService(UnitOfWork);
        var call = new CustomOdataFiler { FunctionName = "xmlExist", Parameters = { "Description", "'it''s'" } };

        service.XmlExist("t", new List<CustomOdataFiler> { call }, out var parameters);

        Assert.Equal("it's", parameters[0]);
    }

    [Theory]
    [InlineData("Description); DROP TABLE Products;--")]
    [InlineData("Description)--")]
    [InlineData("Desc]ription")]
    [InlineData("[Description]")]
    [InlineData("Description\" OR 1=1 --")]
    [InlineData("Descrïption")] // unicode
    [InlineData("Да")]
    [InlineData("Description;")]
    [InlineData("Description'")]
    [InlineData("")]
    [InlineData("Description/")]
    [InlineData("Nope")] // identifier, but not a mapped property
    [InlineData("Category/Nope")]
    [InlineData("Category/Name/Extra")] // string has no property Extra
    public void QueryXmlExist_BadPath_IsRejected(string path)
    {
        var service = new ExposedService(UnitOfWork);
        var call = new CustomOdataFiler { FunctionName = "xmlExist", Parameters = { path, "'x'" } };

        var ex = Assert.Throws<ApiException>(() => service.XmlExist("t", new List<CustomOdataFiler> { call }, out _));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.HttpStatusCode);
    }

    [Theory]
    [InlineData("x")]                       // not quoted
    [InlineData("'x' OR 1=1")]              // more than one literal
    [InlineData("'x') = 1; DROP TABLE a;--")]
    [InlineData("'x'--")]
    [InlineData("'x' 'y'")]
    [InlineData("'x")]
    [InlineData("x'")]
    [InlineData("'")]
    [InlineData("")]
    [InlineData("'a'b'")]                   // lone quote in the middle
    [InlineData("\"a\"b\"")]
    [InlineData("'a\"")]                    // mismatched quotes
    [InlineData("1")]
    public void QueryXmlExist_BadValue_IsRejected(string value)
    {
        var service = new ExposedService(UnitOfWork);
        var call = new CustomOdataFiler { FunctionName = "xmlExist", Parameters = { "Description", value } };

        Assert.Throws<ApiException>(() => service.XmlExist("t", new List<CustomOdataFiler> { call }, out _));
    }

    [Fact]
    public void QueryXmlExist_ValueTooLong_IsRejected()
    {
        var service = new ExposedService(UnitOfWork);
        var call = new CustomOdataFiler { FunctionName = "xmlExist", Parameters = { "Description", "'" + new string('a', 1001) + "'" } };

        Assert.Throws<ApiException>(() => service.XmlExist("t", new List<CustomOdataFiler> { call }, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void QueryXmlExist_WrongArgumentCount_IsRejected(int count)
    {
        var service = new ExposedService(UnitOfWork);
        var call = new CustomOdataFiler { FunctionName = "xmlExist", Parameters = Enumerable.Repeat("'x'", count).ToList() };

        Assert.Throws<ApiException>(() => service.XmlExist("t", new List<CustomOdataFiler> { call }, out _));
    }

    [Fact]
    public void QueryXmlExist_NavigationPath_UsesJoinAliasOfTheServiceHook()
    {
        var service = new ExposedService(UnitOfWork);
        var call = new CustomOdataFiler { FunctionName = "xmlExist", Parameters = { "Category/Name", "'x'" } };

        var sql = service.XmlExist("t", new List<CustomOdataFiler> { call }, out _);

        Assert.Contains("c.Name.exist(", sql);
        Assert.Contains("JOIN Category c", sql);
    }

    [Fact]
    public void GetQueryable_CustomFilterGoesThroughSqlQueryHook()
    {
        var service = new ExposedService(UnitOfWork);

        // the EF repository does not support SqlQuery over an IQueryable, so the hook is reached and reports it
        Assert.Throws<NotSupportedException>(() => service.CustomQueryable(Context.Products, new List<CustomOdataFiler>
        {
            new() { FunctionName = "xmlExist", Parameters = { "Description", "'x'" } },
        }));
        Assert.Throws<NotImplementedException>(() => service.CustomQueryable(Context.Products, new List<CustomOdataFiler>
        {
            new() { FunctionName = "other", Parameters = { "a" } },
        }));
    }

    // --- service plumbing ----------------------------------------------------------------------------------------

    [Fact]
    public void Constructor_LogsAtDebugNotInformation()
    {
        var logger = new FakeLogger<BaseReadService<Product, int>>();
        var service = new ReadOnlyProductService(new TestUnitOfWork(Context).Repository<Product, int>(), logger);

        Assert.NotNull(service);
        Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, logger.LatestRecord.Level);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ReadOnlyProductService(null!, NullLogger<BaseReadService<Product, int>>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ReadOnlyProductService(UnitOfWork.Repository<Product, int>(), null!));
    }

    [Fact]
    public void PopulateAdditionalProperties_ReturnsSameEntity()
    {
        var p = new Product();

        Assert.Same(p, Service.PopulateAdditionalProperties(p));
        Assert.Null(Service.PopulateAdditionalProperties(null));
    }

    [Fact]
    public void GetKeyAndPredicate_RoundTrip()
    {
        Assert.Equal(7, Service.GetKey(new Product { Id = 7 }));
        Assert.True(Service.GetKeyPredicate(7).Compile()(new Product { Id = 7 }));
    }

    private sealed class ReadOnlyProductService(IGenericReadRepository<Product, int> repository, Microsoft.Extensions.Logging.ILogger<BaseReadService<Product, int>> logger)
        : BaseReadService<Product, int>(repository, logger)
    {
        public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;
    }

    /// <summary>Opens the protected members for tests.</summary>
    private sealed class ExposedService(IUnitOfWork unitOfWork) : BaseReadService<Product, int>(unitOfWork.Repository<Product, int>(), NullLogger<BaseReadService<Product, int>>.Instance)
    {
        protected override string[] IncludeProperties => new[] { "Category" };

        public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        public Expression<Func<Product, Product>> Select(string fields) => CreateSelectExpression(fields);

        public Func<Product, Product> NewStatement(string fields) => CreateNewStatement(fields);

        public (FilterQueryOption? Filter, List<CustomOdataFiler>? Custom) Populate(ODataQueryOptions<Product> options) => PopulateFilter(options);

        public string XmlExist(string alias, List<CustomOdataFiler> calls, out List<object> parameters) => QueryXmlExist(alias, calls, out parameters);

        public IQueryable<Product> CustomQueryable(IQueryable<Product> q, IList<CustomOdataFiler> custom) => GetQueryable(q, custom);

        protected override (string Alias, string JoinQuery) CustomQueryJoinQuery(string navProperty) => ("c", $"JOIN {navProperty} c ON 1 = 1");
    }
}
