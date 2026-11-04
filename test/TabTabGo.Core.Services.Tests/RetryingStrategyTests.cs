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
        string? seenByPostUpdate = null;
        Service.PostUpdateHook = _ =>
        {
            // a second context only sees committed data
            using var other = Db.CreateContext();
            seenByPostUpdate = other.Products.Find(4)!.Name;
        };

        await Service.UpdateChanges(4, product, HardwareScope, Ct);

        Assert.Equal("committed first", seenByPostUpdate);
    }
}
