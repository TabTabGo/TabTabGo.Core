using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Core.Services.Tests;

/// <summary>Release review fixes DG-3, V11-1, V11-2 and m6.</summary>
public class ReleaseFixTests : IDisposable
{
    private readonly SqliteFixture _db = new(retrying: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    // --- DG-3: a change that is not tracked is not replayable --------------------------------------------------------------

    [Fact]
    public async Task UpdateChanges_Scoped_UntrackedEntityWithARetry_FailsClosedInsteadOfAnsweringOkForNothing()
    {
        _db.NoTrackingByDefault = true;
        _db.BumpVersion = true;
        _db.SeedWidget();
        var failure = new FailOnceAfterWrite();
        _db.DefaultInterceptors.Add(failure);
        using var context = _db.CreateContext();
        var service = new TestWidgetService(new TestUnitOfWork(context));
        var widget = (await service.GetByKey(1, cancellationToken: Ct))!;
        Assert.Equal(EntityState.Detached, context.Entry(widget).State);
        widget.Name = "patched";

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.UpdateChanges(1, widget, w => w.Tenant == "A", Ct));

        Assert.Equal("RETRY_NOT_REPLAYABLE", ex.ErrorCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.HttpStatusCode);
        Assert.Equal(1, failure.Failures);
        using var fresh = _db.CreateContext();
        Assert.Equal("w1", fresh.Widgets.Single().Name); // nothing committed, and nothing reported as done
    }

    // --- V11-1: a failure before the operation runs is not a retry of the operation -----------------------------------------

    [Fact]
    public async Task UpdateChanges_Scoped_TransientFailureWhileOpeningTheTransaction_StillRunsTheFirstAttemptOnTheTrackedEntity()
    {
        _db.SeedGadget();
        using (var seed = _db.CreateContext())
        {
            seed.Set<GadgetLine>().Add(new GadgetLine { GadgetId = 1, Text = "to remove" });
            seed.SaveChanges();
        }

        var failure = new FailOnceOnTransactionStart();
        _db.DefaultInterceptors.Add(failure);
        using var context = _db.CreateContext();
        var service = new TestGadgetService(new TestUnitOfWork(context));
        var gadget = (await service.GetByKey(1, cancellationToken: Ct))!;
        gadget.Name = "patched";
        gadget.Address.City = "new";
        gadget.Lines.Clear(); // a child deletion is only known to the tracker

        await service.UpdateChanges(1, gadget, g => g.Tenant == "A", Ct);

        Assert.Equal(1, failure.Failures);
        using var fresh = _db.CreateContext();
        var stored = fresh.Gadgets.Include(g => g.Lines).AsNoTracking().Single();
        Assert.Equal("patched", stored.Name);
        Assert.Equal("new", stored.Address.City);
        Assert.Empty(stored.Lines); // lost when the tracker was cleared before the first run of the operation
    }

    // --- V11-2: scoped deletes fail closed over an overridden unscoped method ------------------------------------------------

    private OverridingDeleteProductService DeleteService()
    {
        _db.Seed();
        return new OverridingDeleteProductService(new TestUnitOfWork(_db.CreateContext()), NullLogger<BaseService<Product, int>>.Instance);
    }

    [Fact]
    public async Task Delete_Scoped_WhenDeleteIsOverridden_FailsClosedAndNothingIsDeleted()
    {
        var service = DeleteService();

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.Delete(2, p => p.Tenant == "A", Ct));

        Assert.Equal("DELETE_OVERRIDDEN", ex.ErrorCode);
        Assert.DoesNotContain(nameof(OverridingDeleteProductService), ex.Message);
        Assert.Equal(0, service.OverrideCalls);
        using var fresh = _db.CreateContext();
        Assert.True(fresh.Products.Find(2)!.IsEnabled);
    }

    [Fact]
    public async Task DeleteMany_Scoped_WhenDeleteIsOverridden_FailsClosedAndNothingIsDeleted()
    {
        var service = DeleteService();

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.Delete(new[] { 2, 4 }, p => p.Tenant == "A", Ct));

        Assert.Equal("DELETE_OVERRIDDEN", ex.ErrorCode);
        using var fresh = _db.CreateContext();
        Assert.True(fresh.Products.Find(2)!.IsEnabled);
        Assert.True(fresh.Products.Find(4)!.IsEnabled);
    }

    [Fact]
    public async Task CanDelete_Scoped_WhenCanDeleteIsOverridden_FailsClosed()
    {
        var service = DeleteService();

        var one = await Assert.ThrowsAsync<ApiException>(() => service.CanDelete(2, p => p.Tenant == "A", Ct));
        var many = await Assert.ThrowsAsync<ApiException>(() => service.CanDelete(new[] { 2, 4 }, p => p.Tenant == "A", Ct));

        Assert.Equal("DELETE_OVERRIDDEN", one.ErrorCode);
        Assert.Equal("DELETE_OVERRIDDEN", many.ErrorCode);
    }

    [Fact]
    public async Task Delete_Unscoped_WhenOverridden_RunsTheOverride_AndScopedDeleteWithoutOverrideStillWorks()
    {
        var overridden = DeleteService();
        await overridden.Delete(2, Ct);
        await overridden.Delete(4, (System.Linq.Expressions.Expression<Func<Product, bool>>?)null, Ct);
        Assert.Equal(2, overridden.OverrideCalls); // null scope is the unscoped path, the override runs

        var plain = new TestProductService(new TestUnitOfWork(_db.CreateContext()), NullLogger<BaseService<Product, int>>.Instance);
        await plain.Delete(6, p => p.Tenant == "A", Ct);
        using var fresh = _db.CreateContext();
        Assert.False(fresh.Products.Find(6)!.IsEnabled);
    }

    // --- m6: the type name stays server-side ------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateChanges_Scoped_WhenOverridden_ClientMessageCarriesNoTypeName()
    {
        _db.Seed();
        var service = new OverridingProductService(new TestUnitOfWork(_db.CreateContext()), NullLogger<BaseService<Product, int>>.Instance);
        var product = (await service.GetByKey(3, cancellationToken: Ct))!;

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.UpdateChanges(3, product, p => p.Tenant == "B", Ct));

        Assert.Equal("UPDATECHANGES_OVERRIDDEN", ex.ErrorCode);
        Assert.DoesNotContain(nameof(OverridingProductService), ex.Message);
        Assert.Contains("UpdateChangesCore", ex.Message);
    }

    // --- RV-2: the scoped CanDelete(ids) goes through GetDeleteValidationAsync like the unscoped one --------------------------

    [Fact]
    public async Task CanDelete_Scoped_RunsAnOverriddenGetDeleteValidationAsync()
    {
        _db.Seed();
        var service = new ValidationOverridingProductService(new TestUnitOfWork(_db.CreateContext()), NullLogger<BaseService<Product, int>>.Instance, refuse: 4);

        Assert.False(await service.CanDelete(new[] { 2, 4 }, p => p.Tenant == "A", Ct));
        Assert.True(await service.CanDelete(new[] { 2, 6 }, p => p.Tenant == "A", Ct));
        Assert.True(await service.CanDelete(Array.Empty<int>(), p => p.Tenant == "A", Ct));
    }
}
