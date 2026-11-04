using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Data.EF.Tests;

public class GenericRepositoryTests : RepositoryTestBase
{
    [Fact]
    public async Task Insert_AllOverloads_TrackAddedEntities()
    {
        var one = Repo.Insert(new Product { Name = "one" });
        var two = await Repo.InsertAsync(new Product { Name = "two" }, Ct);
        Repo.Insert(new[] { new Product { Name = "three" } });
        await Repo.InsertAsync(new[] { new Product { Name = "four" } }, Ct);
        await Context.SaveChangesAsync(Ct);

        Assert.Equal("one", one?.Name);
        Assert.Equal("two", two?.Name);
        Assert.Equal(29, await Context.Products.CountAsync(Ct));
    }

    [Fact]
    public async Task Update_Entity_MarksModifiedAndSaves()
    {
        await using var other = Db.CreateContext();
        var detached = await other.Products.AsNoTracking().FirstAsync(p => p.Id == 1, Ct);
        detached.Name = "updated";

        var result = Repo.Update(detached);
        await Context.SaveChangesAsync(Ct);

        Assert.Equal("updated", result?.Name);
        await using var fresh = Db.CreateContext();
        Assert.Equal("updated", (await fresh.Products.FindAsync(new object[] { 1 }, Ct))!.Name);
    }

    [Fact]
    public async Task UpdateAsync_Entity_ReturnsEntityWithoutThreadHop()
    {
        var product = (await Repo.GetByKeyAsync(2, cancellationToken: Ct))!;
        product.Name = "async";

        var result = await Repo.UpdateAsync(product, Ct);
        await Context.SaveChangesAsync(Ct);

        Assert.Same(product, result);
    }

    [Fact]
    public async Task Update_AndUpdateAsync_Lists_Work()
    {
        var items = Repo.Get(p => p.Id <= 2).ToList();
        items.ForEach(p => p.Price = 1);
        Repo.Update(items);
        await Repo.UpdateAsync(items, Ct);
        await Context.SaveChangesAsync(Ct);

        await using var fresh = Db.CreateContext();
        Assert.Equal(2, await fresh.Products.CountAsync(p => p.Price == 1, Ct));
    }

    [Fact]
    public async Task Update_JsonObject_MergesOnlyProvidedProperties_ForEveryMatch()
    {
        Repo.Update(new JsonObject { ["description"] = "bulk" }, p => p.Id <= 3);
        await Context.SaveChangesAsync(Ct);

        await using var fresh = Db.CreateContext();
        var changed = await fresh.Products.Where(p => p.Description == "bulk").ToListAsync(Ct);
        Assert.Equal(3, changed.Count);
        Assert.All(changed, p => Assert.NotEqual(0m, p.Price)); // other fields survive
    }

    [Fact]
    public async Task UpdateAsync_JsonObject_Works()
    {
        await Repo.UpdateAsync(new JsonObject { ["description"] = "later" }, p => p.Id == 4, Ct);
        await Context.SaveChangesAsync(Ct);

        await using var fresh = Db.CreateContext();
        Assert.Equal("later", (await fresh.Products.FindAsync(new object[] { 4 }, Ct))!.Description);
    }

    [Fact]
    public void Update_JsonObject_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Repo.Update(null!, p => true));
        Assert.Throws<ArgumentNullException>(() => Repo.Update(new JsonObject(), null!));
    }

    [Fact]
    public async Task Delete_ByKey_RemovesAndReturnsEntity()
    {
        var deleted = Repo.Delete(5);
        await Context.SaveChangesAsync(Ct);

        Assert.Equal(5, deleted?.Id);
        Assert.Equal(24, await Context.Products.CountAsync(Ct));
    }

    [Fact]
    public void Delete_MissingKey_ReturnsNullInsteadOfThrowing()
    {
        Assert.Null(Repo.Delete(999)); // used to call Remove(null) (N-12)
        Assert.Null(Repo.Delete((Product?)null));
    }

    [Fact]
    public async Task Delete_ByFilterAndQueryable_RemoveMatches()
    {
        Repo.Delete(p => p.Id <= 2);
        Repo.Delete(Context.Products.Where(p => p.Id == 3 || p.Id == 4));
        await Context.SaveChangesAsync(Ct);

        Assert.Equal(21, await Context.Products.CountAsync(Ct));
        Assert.Throws<ArgumentNullException>(() => Repo.Delete((System.Linq.Expressions.Expression<Func<Product, bool>>)null!));
        Assert.Throws<ArgumentNullException>(() => Repo.Delete((IQueryable<Product>)null!));
    }

    [Fact]
    public async Task Delete_ByEntity_Removes()
    {
        var product = (await Repo.GetByKeyAsync(6, cancellationToken: Ct))!;

        Assert.Same(product, Repo.Delete(product));
        await Context.SaveChangesAsync(Ct);
        Assert.Equal(24, await Context.Products.CountAsync(Ct));
    }
}
