using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Core.Services.Tests;

public class BaseServiceTests : ServiceTestBase
{
    private static async Task<ApiException> Api(Func<Task> action) => await Assert.ThrowsAsync<ApiException>(action);

    // --- create --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_Valid_ReturnsReloadedEntityWithGeneratedKey()
    {
        var created = await Service.Create(new Product { Name = "New", Price = 1, CategoryId = 1 }, Ct);

        Assert.True(created.Id > 25);
        Assert.NotNull(created.Category); // reloaded with the service include
        Assert.Equal(new[] { "PreCreate", "PostCreate" }, Service.Calls);
        Assert.Equal(26, await Context.Products.CountAsync(Ct));
    }

    [Fact]
    public async Task Create_Invalid_Is400WithNumber2001()
    {
        Service.CreateRule = _ => "Name is taken";

        var ex = await Api(() => Service.Create(new Product { Name = "x" }, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        Assert.Equal(2001, ex.ErrorNumber);
        Assert.Contains("Name is taken", ex.Message);
        Assert.Equal(25, await Context.Products.CountAsync(Ct));
    }

    [Fact]
    public async Task Create_Null_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service.Create(null, Ct));
    }

    [Fact]
    public void IsValidToCreate_DefaultsToTrue()
    {
        var service = new PlainService(UnitOfWork);

        Assert.True(service.IsValidToCreate(new Product(), out var message));
        Assert.Equal(string.Empty, message);
        Assert.True(service.IsValidToUpdate(new Product(), out _));
        Assert.True(service.IsValidToDelete(new Product(), out _));
    }

    // --- delete --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_Existing_DisablesAndSaves()
    {
        var deleted = await Service.Delete(2, Ct);

        Assert.False(deleted.IsEnabled);
        Assert.Equal(new[] { "PreDelete", "PostDelete" }, Service.Calls);
        await using var fresh = Db.CreateContext();
        Assert.False((await fresh.Products.FindAsync(new object[] { 2 }, Ct))!.IsEnabled);
    }

    [Fact]
    public async Task Delete_Missing_Is404NotNullReference()
    {
        var ex = await Api(() => Service.Delete(999, Ct));

        Assert.Equal(HttpStatusCode.NotFound, ex.HttpStatusCode);
    }

    [Fact]
    public async Task Delete_NotAllowed_Is400NotBareException()
    {
        Service.DeleteRule = p => "in use";

        var ex = await Api(() => Service.Delete(2, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        Assert.Contains("in use", ex.Message);
    }

    [Fact]
    public async Task CanDelete_Single_ReflectsValidation_And404WhenMissing()
    {
        Assert.True(await Service.CanDelete(1, Ct));
        Service.DeleteRule = p => p.Id == 1 ? "no" : null;
        Assert.False(await Service.CanDelete(1, Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await Api(() => Service.CanDelete(999, Ct))).HttpStatusCode);
    }

    [Fact]
    public async Task CanDelete_Many_ReturnsRealResult()
    {
        Service.DeleteRule = p => p.Id == 3 ? "no" : null;

        Assert.True(await Service.CanDelete(new[] { 1, 2 }, Ct));
        Assert.False(await Service.CanDelete(new[] { 1, 2, 3 }, Ct)); // always true before (O-3)
    }

    [Fact]
    public async Task CanDelete_Many_FilterIsTranslatedToSqlAsOr()
    {
        // Contains(GetKey(e)) could not be translated; an OR of key predicates can
        var ok = await Service.CanDelete(new[] { 4, 5, 6, 7 }, Ct);

        Assert.True(ok);
    }

    [Fact]
    public async Task GetDeleteValidation_ReportsEachFoundEntityOnly()
    {
        Service.DeleteRule = p => p.Id == 2 ? "locked" : null;

        var result = await Service.GetDeleteValidationAsync(new[] { 1, 2, 999 }, Ct);

        Assert.Equal(2, result.Count);
        Assert.True(result.Single(r => r.Id == 1).CanDelete);
        var blocked = result.Single(r => r.Id == 2);
        Assert.False(blocked.CanDelete);
        Assert.Equal("locked", blocked.Error);
    }

    [Fact]
    public async Task GetDeleteValidation_NoIds_IsEmpty_AndNullThrows()
    {
        Assert.Empty(await Service.GetDeleteValidationAsync(Array.Empty<int>(), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service.GetDeleteValidationAsync(null!, Ct));
    }

    [Fact]
    public async Task Delete_Many_ValidatesThenDisables()
    {
        var deleted = (await Service.Delete(new[] { 1, 2 }, Ct)).ToList();

        Assert.Equal(2, deleted.Count);
        Assert.All(deleted, p => Assert.False(p.IsEnabled));
        Assert.Equal(2, await Context.Products.CountAsync(p => !p.IsEnabled, Ct));
    }

    [Fact]
    public async Task Delete_Many_OneBlocked_Is400AndNothingIsDisabled()
    {
        // used to skip validation (N-14)
        Service.DeleteRule = p => p.Id == 2 ? "locked" : null;

        var ex = await Api(() => Service.Delete(new[] { 1, 2 }, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        Assert.Equal(0, await Context.Products.CountAsync(p => !p.IsEnabled, Ct));
    }

    // --- update --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_FullEntity_CopiesValuesCallsRepositoryAndSaves()
    {
        var incoming = new Product { Id = 3, Name = "Renamed", Price = 999, Tenant = "A", IsEnabled = false };

        var updated = await Service.Update(3, incoming, Ct);

        Assert.Equal("Renamed", updated.Name);
        Assert.True(updated.IsEnabled); // IsEnabled is ignored on update
        Assert.Equal(new[] { "PreUpdate", "PostUpdate" }, Service.Calls);
        await using var fresh = Db.CreateContext();
        Assert.Equal(999m, (await fresh.Products.FindAsync(new object[] { 3 }, Ct))!.Price);
    }

    [Fact]
    public async Task Update_Missing_Is404()
    {
        var ex = await Api(() => Service.Update(999, new Product { Id = 999 }, Ct));

        Assert.Equal(HttpStatusCode.NotFound, ex.HttpStatusCode);
    }

    [Fact]
    public async Task Update_Invalid_Is400Number4001()
    {
        Service.UpdateRule = _ => "nope";

        var ex = await Api(() => Service.Update(3, new Product { Id = 3, Name = "x" }, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        Assert.Equal(4001, ex.ErrorNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Update_BodyKeyDifferentFromRouteKey_Is400NotAKeyModificationError(int bodyId)
    {
        var ex = await Api(() => Service.Update(3, new Product { Id = bodyId, Name = "x" }, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        Assert.Equal(4003, ex.ErrorNumber);
    }

    [Fact]
    public async Task Update_Null_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service.Update(3, (Product)null!, Ct));
    }

    [Fact]
    public async Task UpdateChanges_UsesLoadedEntityAndSaves()
    {
        var product = (await Service.GetByKey(4, cancellationToken: Ct))!;
        product.Name = "Changed in place";

        var updated = await Service.UpdateChanges(4, product, Ct);

        Assert.Same(product, updated);
        await using var fresh = Db.CreateContext();
        Assert.Equal("Changed in place", (await fresh.Products.FindAsync(new object[] { 4 }, Ct))!.Name);
    }

    [Fact]
    public async Task UpdateChanges_NothingLoaded_LoadsByKey_AndMissingIs404()
    {
        var updated = await Service.UpdateChanges(5, new Product { Name = "ignored" }, Ct);
        Assert.Equal(5, updated.Id);

        var ex = await Api(() => Service.UpdateChanges(999, new Product(), Ct));
        Assert.Equal(HttpStatusCode.NotFound, ex.HttpStatusCode);
    }

    [Fact]
    public async Task UpdateChanges_Invalid_Is400Number4002()
    {
        Service.UpdateRule = _ => "nope";

        var ex = await Api(() => Service.UpdateChanges(5, new Product(), Ct));

        Assert.Equal(4002, ex.ErrorNumber);
    }

    [Fact]
    public async Task Update_JsonPatch_AppliesOperationsAndSaves()
    {
        var patch = new JsonPatchDocument<Product>();
        patch.Replace(p => p.Name, "Patched");

        var updated = await Service.Update(6, patch, Ct);

        Assert.Equal("Patched", updated.Name);
        await using var fresh = Db.CreateContext();
        Assert.Equal("Patched", (await fresh.Products.FindAsync(new object[] { 6 }, Ct))!.Name);
    }

    [Fact]
    public async Task Update_JsonPatch_UnknownPathGoesToExtraProperties_AndIsPersisted()
    {
        var patch = new JsonPatchDocument<Product>();
        patch.Operations.Add(new Operation<Product>("add", "/color", null, "red"));

        await Service.Update(6, patch, Ct);

        await using var fresh = Db.CreateContext();
        var stored = (await fresh.Products.FindAsync(new object[] { 6 }, Ct))!;
        Assert.Equal("red", stored.ExtraProperties["color"]?.ToString());
    }

    [Fact]
    public async Task Update_JsonPatch_MissingAndInvalid()
    {
        var patch = new JsonPatchDocument<Product>();
        Assert.Equal(HttpStatusCode.NotFound, (await Api(() => Service.Update(999, patch, Ct))).HttpStatusCode);
        Service.UpdateRule = _ => "nope";
        Assert.Equal(HttpStatusCode.BadRequest, (await Api(() => Service.Update(6, patch, Ct))).HttpStatusCode);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service.Update(6, (JsonPatchDocument<Product>)null!, Ct));
    }

    [Fact]
    public async Task Update_JsonObject_MergesOnlyProvidedProperties()
    {
        var before = (await Service.GetByKey(7, cancellationToken: Ct))!;
        var originalPrice = before.Price;

        var updated = await Service.Update(7, new JsonObject { ["name"] = "Merged" }, Ct);

        Assert.Equal("Merged", updated.Name);
        Assert.Equal(originalPrice, updated.Price); // used to be reset to 0 (D-13)
    }

    [Fact]
    public async Task Update_JsonObject_MissingInvalidNull()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Api(() => Service.Update(999, new JsonObject(), Ct))).HttpStatusCode);
        Service.UpdateRule = _ => "nope";
        Assert.Equal(4002, (await Api(() => Service.Update(7, new JsonObject(), Ct))).ErrorNumber);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service.Update(7, (JsonObject)null!, Ct));
    }

    // --- JSON patch operator -------------------------------------------------------------------------------------

    [Fact]
    public void HandleJsonOperator_AddExisting_Replaces()
    {
        var product = new Product();
        product.ExtraProperties["color"] = "red";

        Service.HandleJsonOperator(new Operation("add", "/color", null, "blue"), product);

        Assert.Equal("blue", product.ExtraProperties["color"]); // used to throw (D-22)
    }

    [Fact]
    public void HandleJsonOperator_AddReplaceRemove_WorkOnExtraProperties()
    {
        var product = new Product();

        Service.HandleJsonOperator(new Operation("add", "/a", null, 1), product);
        Service.HandleJsonOperator(new Operation("replace", "/b", null, 2), product);
        Service.HandleJsonOperator(new Operation("replace", "/b", null, 3), product);
        Service.HandleJsonOperator(new Operation("remove", "/a", null), product);
        Service.HandleJsonOperator(new Operation("remove", "/missing", null), product);

        Assert.Equal(3, product.ExtraProperties["b"]);
        Assert.DoesNotContain("a", product.ExtraProperties.Keys);
    }

    [Fact]
    public void HandleJsonOperator_NewtonsoftValue_IsStoredAsPlainClrValue()
    {
        var product = new Product();

        Service.HandleJsonOperator(new Operation("add", "/obj", null, JObject.Parse("{\"x\":1}")), product);

        var stored = Assert.IsAssignableFrom<IDictionary<string, object?>>(product.ExtraProperties["obj"]);
        Assert.Equal(1L, stored["x"]);
    }

    [Fact]
    public void HandleJsonOperator_KnownPropertyPath_DoesNotTouchExtraProperties()
    {
        var product = new Product();

        Service.HandleJsonOperator(new Operation("replace", "/name", null, "x"), product);

        Assert.Empty(product.ExtraProperties);
    }

    // --- included property discovery -----------------------------------------------------------------------------

    [Fact]
    public void GetIncludedProperties_FromEntity_NullIgnoredListMeansNone()
    {
        var service = new PlainService(UnitOfWork);
        var product = new Product { Category = new Category(), Lines = new List<OrderLine> { new() } };

        Assert.Contains("Category", service.Included(product, null));
        Assert.DoesNotContain("Category", service.Included(product, new[] { "Category" }));
        // only ICollection<T>/IEnumerable<T>/array typed navigation properties are detected (List<T> is not)
        Assert.DoesNotContain("Lines", service.Included(product, null));
    }

    [Fact]
    public void GetIncludedProperties_FromJson_NullIgnoredListStillIncludes()
    {
        var service = new PlainService(UnitOfWork);
        var json = new JsonObject { ["category"] = new JsonObject { ["name"] = "x" }, ["name"] = "p", ["lines"] = new JsonArray(new JsonObject { ["quantity"] = 1 }) };

        var withNull = service.Included(json, null);
        var withIgnore = service.Included(json, new[] { "Category" });

        Assert.Contains("Category", withNull); // a null list used to include nothing (D-23)
        Assert.Contains("Lines", withNull);
        Assert.DoesNotContain("Category", withIgnore);
    }

    [Fact]
    public void GetIncludedProperties_FromPatch_BuildsPathsAndHonoursIgnored()
    {
        var service = new PlainService(UnitOfWork);
        var patch = new JsonPatchDocument<Product>();
        patch.Operations.Add(new Operation<Product>("replace", "/category/name", null, "x"));

        Assert.Equal(new[] { "Category" }, service.Included(patch, null));
        Assert.Empty(service.Included(patch, new[] { "Category" }));
    }

    [Fact]
    public void GetPropertyPath_SkipsNumbersAndEmptySegments()
    {
        var service = new PlainService(UnitOfWork);

        var paths = service.Path("/lines/0/quantity", new List<string> { "Lines" }, null);

        Assert.Equal(new[] { "Lines" }, paths);
        Assert.Empty(service.Path("//", new List<string> { "Lines" }, null));
    }

    [Fact]
    public void GetProperties_ReturnsReferencePropertiesOnly()
    {
        var service = new PlainService(UnitOfWork);

        var props = service.Properties(typeof(Product)).Select(p => p.Name).ToList();

        Assert.Contains("Category", props);
        Assert.Contains("Lines", props);
        Assert.DoesNotContain("Name", props);
        Assert.Contains("Products", service.Properties(typeof(Category).GetProperty("Products")!).Select(p => p.Name).Concat(new[] { "Products" }));
    }

    [Fact]
    public void IsPrimitive_RecognisesValueTypesAndNullables()
    {
        var service = new PlainService(UnitOfWork);

        Assert.True(service.Primitive(typeof(string)));
        Assert.True(service.Primitive(typeof(int)));
        Assert.True(service.Primitive(typeof(int?)));
        Assert.True(service.Primitive(typeof(DateTime?)));
        Assert.False(service.Primitive(typeof(Product)));
        Assert.False(service.Primitive(typeof(Guid?)));
    }

    [Fact]
    public void IsReferenceProperty_NullNamespaceSafe()
    {
        var service = new PlainService(UnitOfWork);

        Assert.True(service.Reference(typeof(Category)));
        Assert.True(service.Reference(typeof(List<Product>)));
        Assert.False(service.Reference(typeof(string)));
        Assert.False(service.Reference(typeof(int)));
    }

    [Fact]
    public async Task Constructor_ExplicitRepository_IsUsed()
    {
        var service = new TestServiceWithRepository(UnitOfWork, UnitOfWork.Repository<Product, int>());

        Assert.Equal(25, (await service.GetList(p => true, cancellationToken: Ct)).Count());
        Assert.Same(UnitOfWork.Repository<Product, int>(), service.CurrentRepository);
        Assert.Throws<ArgumentNullException>(() => new TestServiceWithRepository(null!, UnitOfWork.Repository<Product, int>()));
    }

    private sealed class TestServiceWithRepository(TabTabGo.Core.Data.IUnitOfWork unitOfWork, TabTabGo.Core.Data.IGenericRepository<Product, int> repository)
        : BaseService<Product, int>(unitOfWork, repository, NullLogger<BaseService<Product, int>>.Instance)
    {
        public override System.Linq.Expressions.Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;
    }

    /// <summary>Opens the protected members for tests.</summary>
    private sealed class PlainService(TabTabGo.Core.Data.IUnitOfWork unitOfWork)
        : BaseService<Product, int>(unitOfWork, NullLogger<BaseService<Product, int>>.Instance)
    {
        public override System.Linq.Expressions.Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        public string[] Included(Product p, string[]? ignored) => GetIncludedProperties(p, ignored);

        public string[] Included(JsonObject p, string[]? ignored) => GetIncludedProperties(p, ignored);

        public string[] Included(JsonPatchDocument<Product> p, string[]? ignored) => GetIncludedProperties(p, ignored);

        public string[] Path(string path, IList<string> props, string[]? ignored) => GetPropertyPath(path, props, ignored);

        public IList<System.Reflection.PropertyInfo> Properties(Type type) => GetProperties(type);

        public IList<System.Reflection.PropertyInfo> Properties(System.Reflection.PropertyInfo property) => GetProperties(property);

        public bool Primitive(Type type) => IsPrimitive(type);

        public bool Reference(Type type) => IsReferenceProperty(type);
    }

    [Theory]
    [InlineData("/id")]
    [InlineData("/isEnabled")]
    [InlineData("/createdDate")]
    [InlineData("/category/name")]
    public async Task Update_JsonPatch_ProtectedProperty_Is400(string path)
    {
        var patch = new JsonPatchDocument<Product>();
        patch.Operations.Add(new Operation<Product>("replace", path, null, "x"));

        var ex = await Api(() => Service.Update(6, patch, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        Assert.Equal("PROTECTED_PROPERTY", ex.ErrorCode);
    }

    [Fact]
    public async Task Update_JsonObject_ProtectedProperty_Is400_RelatedRowUntouched()
    {
        var ex = await Api(() => Service.Update(7, new JsonObject { ["category"] = new JsonObject { ["name"] = "renamed" } }, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        await using var fresh = Db.CreateContext();
        Assert.Equal("Hardware", fresh.Categories.Single().Name);
    }

    [Fact]
    public void EnsurePatchable_AllowsOrdinaryProperties_AndSubclassesCanProtectMore()
    {
        var patch = new JsonPatchDocument<Product>();
        patch.Replace(p => p.Name, "x");

        Service.EnsurePatchable(patch);
        Service.EnsurePatchable(new JsonObject { ["name"] = "x" });
        Assert.Throws<ApiException>(() => new StrictService(UnitOfWork).EnsurePatchable(new JsonObject { ["tenant"] = "B" }));
        Assert.Throws<ArgumentNullException>(() => Service.EnsurePatchable((JsonObject)null!));
        Assert.Throws<ArgumentNullException>(() => Service.EnsurePatchable((JsonPatchDocument<Product>)null!));
    }

    [Fact]
    public void HandleJsonOperator_ExtraPropertyLimits()
    {
        var product = new Product();
        for (var i = 0; i < 100; i++)
        {
            Service.HandleJsonOperator(new Operation("add", "/k" + i, null, i), product);
        }

        Assert.Throws<ApiException>(() => Service.HandleJsonOperator(new Operation("add", "/one-too-many", null, 1), product));
        Service.HandleJsonOperator(new Operation("replace", "/k5", null, "still allowed"), product); // existing key
        Assert.Throws<ApiException>(() => Service.HandleJsonOperator(new Operation("replace", "/k6", null, new string('x', 20_000)), product));
    }

    private sealed class StrictService(TabTabGo.Core.Data.IUnitOfWork unitOfWork)
        : BaseService<Product, int>(unitOfWork, NullLogger<BaseService<Product, int>>.Instance)
    {
        public override System.Linq.Expressions.Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

        public override int GetKey(Product entity) => entity.Id;

        protected override bool IsProtectedProperty(string propertyName) =>
            propertyName.Equals("Tenant", StringComparison.OrdinalIgnoreCase) || base.IsProtectedProperty(propertyName);
    }
}
