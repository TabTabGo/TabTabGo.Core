using System.Linq.Expressions;
using System.Net;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Core.Services.Tests;

/// <summary>H2: a scoped update must work on a provider whose execution strategy retries (SQL Server, Npgsql).</summary>
public class RetryingStrategyTests() : ServiceTestBase(seed: true, retrying: true)
{
    private static Expression<Func<Product, bool>> HardwareScope => p => p.Category!.Name == "Hardware";

    [Fact]
    public async Task UpdateChanges_Scoped_WorksWithARetryingExecutionStrategy()
    {
        var product = (await Service.GetByKey(3, cancellationToken: Ct))!;
        product.Name = "retrying";

        await Service.UpdateChanges(3, product, HardwareScope, Ct); // InvalidOperationException before: user transaction outside the strategy

        await using var fresh = Db.CreateContext();
        Assert.Equal("retrying", fresh.Products.Find(3)!.Name);
        Assert.False(UnitOfWork.HasActiveTransaction);
    }

    [Fact]
    public async Task UpdateChanges_Scoped_OutOfScopeWithARetryingStrategy_RollsBackAndPostUpdateDoesNotRun()
    {
        int otherCategory;
        await using (var context = Db.CreateContext())
        {
            var other = new Category { Name = "Other" };
            context.Categories.Add(other);
            await context.SaveChangesAsync(Ct);
            otherCategory = other.Id;
        }

        var product = (await Service.GetByKey(2, cancellationToken: Ct))!;
        product.CategoryId = otherCategory;
        product.Name = "moved";

        var ex = await Assert.ThrowsAsync<ApiException>(() => Service.UpdateChanges(2, product, HardwareScope, Ct));

        Assert.Equal(HttpStatusCode.NotFound, ex.HttpStatusCode);
        await using var fresh = Db.CreateContext();
        Assert.Equal("Product 02", fresh.Products.Find(2)!.Name);
        Assert.DoesNotContain("PostUpdate", Service.Calls);
    }

    [Fact]
    public async Task UpdateChanges_Scoped_PostUpdateRunsOnlyAfterTheCommit()
    {
        var product = (await Service.GetByKey(4, cancellationToken: Ct))!;
        product.Name = "committed first";
        var transactionOpenInPostUpdate = new List<bool>();
        Service.PostUpdateHook = _ => transactionOpenInPostUpdate.Add(UnitOfWork.HasActiveTransaction);

        await Service.UpdateChanges(4, product, HardwareScope, Ct);

        // once, and after the commit (a second context on the shared SQLite connection would see uncommitted rows)
        Assert.Equal(new[] { false }, transactionOpenInPostUpdate);
    }

    private (FailOnceAfterWrite Failure, Services.BaseService<Product, int> Service, TestProductService Typed) ServiceWithFailureBeforeCommit()
    {
        var failure = new FailOnceAfterWrite();
        Db.DefaultInterceptors.Add(failure);
        var context = Db.CreateContext();
        var typed = new TestProductService(new TestUnitOfWork(context),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Services.BaseService<Product, int>>.Instance);
        return (failure, typed, typed);
    }

    [Fact]
    public async Task UpdateChanges_Scoped_TransientFailureBeforeTheCommit_AppliesTheChangeOnceAndFiresPostUpdateAfterIt()
    {
        var (failure, service, typed) = ServiceWithFailureBeforeCommit();
        var product = (await service.GetByKey(6, cancellationToken: Ct))!;
        product.Name = "after retry";
        product.Price = 999;
        var postUpdateSaw = new List<string>();
        typed.PostUpdateHook = _ =>
        {
            using var other = Db.CreateContext();
            postUpdateSaw.Add(other.Products.Find(6)!.Name);
        };

        await service.UpdateChanges(6, product, HardwareScope, Ct);

        Assert.Equal(1, failure.Failures);
        await using var fresh = Db.CreateContext();
        var stored = fresh.Products.Find(6)!;
        Assert.Equal("after retry", stored.Name);
        Assert.Equal(999, stored.Price);
        Assert.Equal(new[] { "after retry" }, postUpdateSaw); // once, and only once the commit made the row visible
        Assert.Equal(1, typed.Calls.Count(c => c == "PostUpdate"));
    }

    [Fact]
    public async Task UpdateChanges_Scoped_ViolatingChangeIsStillRejectedOnTheRetryAttempt()
    {
        int otherCategory;
        await using (var context = Db.CreateContext())
        {
            var other = new Category { Name = "Other" };
            context.Categories.Add(other);
            await context.SaveChangesAsync(Ct);
            otherCategory = other.Id;
        }

        var (failure, service, typed) = ServiceWithFailureBeforeCommit();
        var product = (await service.GetByKey(8, cancellationToken: Ct))!;
        product.CategoryId = otherCategory;
        product.Name = "escaped";

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.UpdateChanges(8, product, HardwareScope, Ct));

        Assert.Equal(1, failure.Failures);
        Assert.Equal(HttpStatusCode.NotFound, ex.HttpStatusCode);
        await using var fresh = Db.CreateContext();
        var stored = fresh.Products.Find(8)!;
        Assert.Equal("Product 08", stored.Name);
        Assert.NotEqual(otherCategory, stored.CategoryId);
        Assert.DoesNotContain("PostUpdate", typed.Calls);
    }
}
