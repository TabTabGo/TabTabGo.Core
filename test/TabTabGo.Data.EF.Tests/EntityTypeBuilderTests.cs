using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using TabTabGo.Core.TestKit;

namespace TabTabGo.Data.EF.Tests;

public class EntityTypeBuilderTests : IDisposable
{
    private readonly SqliteFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string RawExtraProperties(int id)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT ExtraProperties FROM Products WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return (string)command.ExecuteScalar()!;
    }

    private async Task<int> AddProductAsync(Action<Product>? configure = null)
    {
        await using var context = _db.CreateContext();
        var product = new Product { Name = "p" };
        configure?.Invoke(product);
        context.Products.Add(product);
        await context.SaveChangesAsync(Ct);
        return product.Id;
    }

    [Fact]
    public async Task ExtraProperties_StoredCompact()
    {
        var id = await AddProductAsync(p => p.ExtraProperties["a"] = 1);

        Assert.Equal("{\"a\":1}", RawExtraProperties(id)); // indented before (D-20)
    }

    [Fact]
    public async Task ExtraProperties_NullValueKept()
    {
        var id = await AddProductAsync(p => p.ExtraProperties["gone"] = null);

        Assert.Equal("{\"gone\":null}", RawExtraProperties(id)); // the key used to be dropped silently

        await using var context = _db.CreateContext();
        Assert.True((await context.Products.FindAsync(new object[] { id }, Ct))!.ExtraProperties.ContainsKey("gone"));
    }

    [Fact]
    public async Task ExtraProperties_ReadsTheOldIndentedFormat()
    {
        var id = await AddProductAsync();
        await using (var command = _db.Connection.CreateCommand())
        {
            command.CommandText = "UPDATE Products SET ExtraProperties = $v WHERE Id = $id";
            command.Parameters.AddWithValue("$v", "{\n  \"old\": \"format\"\n}");
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        await using var context = _db.CreateContext();
        var loaded = await context.Products.FindAsync(new object[] { id }, Ct);

        Assert.Equal("format", loaded!.ExtraProperties["old"]?.ToString());
    }

    [Fact]
    public async Task ExtraProperties_EmptyOrNullColumn_ReadsAsEmptyDictionary()
    {
        var id = await AddProductAsync();
        await using (var command = _db.Connection.CreateCommand())
        {
            command.CommandText = "UPDATE Products SET ExtraProperties = '' WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        await using var context = _db.CreateContext();

        Assert.Empty((await context.Products.FindAsync(new object[] { id }, Ct))!.ExtraProperties);
    }

    [Fact]
    public async Task ExtraProperties_InPlaceEdit_IsDetectedAndSaved()
    {
        // the dictionary used to be compared by reference, so edits were never saved (N-19)
        var id = await AddProductAsync(p => p.ExtraProperties["a"] = "one");
        await using (var context = _db.CreateContext())
        {
            var product = (await context.Products.FindAsync(new object[] { id }, Ct))!;
            product.ExtraProperties["a"] = "two";
            product.ExtraProperties["b"] = true;

            Assert.Equal(1, await context.SaveChangesAsync(Ct));
        }

        Assert.Equal("{\"a\":\"two\",\"b\":true}", RawExtraProperties(id));
    }

    [Fact]
    public async Task ExtraProperties_NoEdit_NoUpdate()
    {
        var id = await AddProductAsync(p => p.ExtraProperties["a"] = "one");
        await using var context = _db.CreateContext();
        _ = await context.Products.FindAsync(new object[] { id }, Ct);

        Assert.Equal(0, await context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task ExtraProperties_Snapshot_IsNotTheSameInstanceAsTheLiveDictionary()
    {
        var id = await AddProductAsync(p => p.ExtraProperties["a"] = "one");
        await using var context = _db.CreateContext();
        var product = (await context.Products.FindAsync(new object[] { id }, Ct))!;
        var entry = context.Entry(product).Property(p => p.ExtraProperties);

        product.ExtraProperties["a"] = "changed";

        Assert.True(context.ChangeTracker.HasChanges());
        Assert.Equal("one", entry.OriginalValue!["a"]?.ToString());
    }

    [Fact]
    public async Task EntityBuilder_JObjectExtraProperty_PersistedCorrectly()
    {
        // STJ wrote a Newtonsoft JObject as nested empty arrays (N-20)
        var id = await AddProductAsync(p => p.ExtraProperties["j"] = JObject.Parse("{\"x\":1,\"y\":[true,\"s\"]}"));

        Assert.Equal("{\"j\":{\"x\":1,\"y\":[true,\"s\"]}}", RawExtraProperties(id));
    }

    [Fact]
    public async Task EntityBuilder_JsonNodeAndNestedValues_RoundTrip()
    {
        var id = await AddProductAsync(p =>
        {
            p.ExtraProperties["node"] = new JsonObject { ["k"] = "v" };
            p.ExtraProperties["dict"] = new Dictionary<string, object?> { ["n"] = 5 };
        });

        await using var context = _db.CreateContext();
        var loaded = (await context.Products.FindAsync(new object[] { id }, Ct))!;

        Assert.Contains("\"k\":\"v\"", loaded.ExtraProperties["node"]!.ToString());
        Assert.Contains("\"n\":5", loaded.ExtraProperties["dict"]!.ToString());
    }

    [Fact]
    public void EntityBuilder_AuditColumnsHaveMaxLength50()
    {
        using var context = _db.CreateContext();
        var entity = context.Model.FindEntityType(typeof(Product))!;

        Assert.Equal(50, entity.FindProperty(nameof(Product.CreatedBy))!.GetMaxLength());
        Assert.Equal(50, entity.FindProperty(nameof(Product.UpdatedBy))!.GetMaxLength());
    }

    [Fact]
    public void EntityBuilder_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TabTabGo.Data.EF.Extensions.EntityTypeBuilder.EntityBuilder<Product>(null!));
    }

    [Fact]
    public void EntityBuilder_OnInMemoryProvider_AlsoWorks()
    {
        var fixture = new InMemoryFixture();
        using (var context = fixture.CreateContext())
        {
            context.Products.Add(new Product { Name = "m", ExtraProperties = { ["a"] = 1 } });
            context.SaveChanges();
        }

        using var read = fixture.CreateContext();
        Assert.Equal("1", read.Products.Single().ExtraProperties["a"]?.ToString());
    }
}
