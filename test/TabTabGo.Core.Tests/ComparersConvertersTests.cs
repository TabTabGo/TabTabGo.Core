using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TabTabGo.Core.Attributes;
using TabTabGo.Core.Comparer;
using TabTabGo.Core.Converters;
using TabTabGo.Core.Entities;

namespace TabTabGo.Core.Tests;

public class ComparersConvertersTests
{
    public class Item : Entity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public Item? Child { get; set; }

        public List<int> Numbers { get; set; } = new();

        public string this[int index] => index.ToString();
    }

    // --- EntityComparer ---------------------------------------------------------------------------------------------

    [Fact]
    public void EntityComparer_EqualAndDifferent_RecordsDifferences()
    {
        // it compared nothing at all before: GetProperties(BindingFlags.Public) alone returns no property (N-23)
        using var comparer = new EntityComparer();

        Assert.True(comparer.Equals(new Item { Id = 1, Name = "a" }, new Item { Id = 1, Name = "a" }));
        Assert.False(comparer.Equals(new Item { Id = 1, Name = "a" }, new Item { Id = 1, Name = "b" }));
        Assert.Equal(new[] { "Name" }, comparer.GetDifferentProeprties());
    }

    [Fact]
    public void EntityComparer_IgnoresAuditFieldsIndexersAndCollections()
    {
        using var comparer = new EntityComparer();
        var a = new Item { Id = 1, CreatedBy = "x", UpdatedDate = DateTimeOffset.UnixEpoch, Numbers = { 1 } };
        var b = new Item { Id = 1, CreatedBy = "y", UpdatedDate = DateTimeOffset.UtcNow, Numbers = { 2 } };
        a.ExtraProperties["k"] = 1;

        Assert.True(comparer.Equals(a, b));
    }

    [Fact]
    public void EntityComparer_NullHandling_NestedEntities()
    {
        using var comparer = new EntityComparer();

        Assert.True(comparer.Equals(null, null));
        Assert.False(comparer.Equals(new Item(), null));
        Assert.False(comparer.Equals(null, new Item()));
        var same = new Item();
        Assert.True(comparer.Equals(same, same));
        Assert.True(comparer.Equals(new Item { Child = new Item { Name = "c" } }, new Item { Child = new Item { Name = "c" } }));
        Assert.False(comparer.Equals(new Item { Child = new Item { Name = "c" } }, new Item { Child = new Item { Name = "d" } }));
        Assert.False(comparer.Equals(new Item { Child = new Item() }, new Item { Child = null }));
        Assert.False(comparer.Equals(new Item { Child = null }, new Item { Child = new Item() }));
        Assert.Contains("Child", comparer.GetDifferentProeprties());
    }

    [Fact]
    public void EntityComparer_HashCodeAndDispose()
    {
        var item = new Item();
        var comparer = new EntityComparer();
        comparer.Equals(new Item { Id = 1 }, new Item { Id = 2 });

        Assert.Equal(item.GetHashCode(), comparer.GetHashCode(item));
        comparer.Dispose();
        Assert.Empty(comparer.DifferentProperties);
        Assert.Empty(comparer.GetDifferentProeprties());
    }

    // --- JsonNodeComparer -------------------------------------------------------------------------------------------

    [Fact]
    public void JsonNodeComparer_PropertyOrderAndNumberFormattingDoNotMatter()
    {
        using var comparer = new JsonNodeComparer();

        Assert.True(comparer.Equals(JsonNode.Parse("{\"a\":1,\"b\":2}"), JsonNode.Parse("{\"b\":2,\"a\":1.0}")));
        Assert.False(comparer.Equals(JsonNode.Parse("{\"a\":1}"), JsonNode.Parse("{\"a\":2}")));
    }

    [Fact]
    public void JsonNodeComparer_IgnoredProperties_AreIgnoredAtAnyDepth()
    {
        using var comparer = new JsonNodeComparer("updated", "id");

        Assert.True(comparer.Equals(
            JsonNode.Parse("{\"id\":1,\"name\":\"x\",\"child\":{\"updated\":\"t1\",\"v\":1},\"list\":[{\"id\":7,\"v\":1}]}"),
            JsonNode.Parse("{\"id\":2,\"name\":\"x\",\"child\":{\"updated\":\"t2\",\"v\":1},\"list\":[{\"id\":8,\"v\":1}]}")));
        Assert.False(comparer.Equals(JsonNode.Parse("{\"id\":1,\"name\":\"x\"}"), JsonNode.Parse("{\"id\":1,\"name\":\"y\"}")));
    }

    [Fact]
    public void JsonNodeComparer_RecordsTheDifferentProperties()
    {
        using var comparer = new JsonNodeComparer();

        var equal = comparer.Equals(JsonNode.Parse("{\"a\":1,\"b\":2,\"c\":3}"), JsonNode.Parse("{\"a\":1,\"b\":9,\"d\":4}"));

        Assert.False(equal);
        Assert.Equal(new[] { "b", "c", "d" }, comparer.GetDifferentProperties().OrderBy(x => x));
        var b = comparer.DifferentProperties.Single(p => p.propName == "b");
        Assert.Equal(2, b.actualValue!.GetValue<int>());
        Assert.Equal(9, b.expectedValue!.GetValue<int>());
        Assert.Null(comparer.DifferentProperties.Single(p => p.propName == "d").actualValue);
        comparer.Equals(JsonNode.Parse("{\"a\":1}"), JsonNode.Parse("{\"a\":1}"));
        Assert.Empty(comparer.DifferentProperties); // reset per comparison
    }

    [Fact]
    public void JsonNodeComparer_NullsAndSameInstance()
    {
        using var comparer = new JsonNodeComparer();
        var node = JsonNode.Parse("{\"a\":1}");

        Assert.True(comparer.Equals(null, null));
        Assert.True(comparer.Equals(node, node));
        Assert.False(comparer.Equals(node, null));
        Assert.False(comparer.Equals(null, node));
        Assert.False(comparer.Equals(JsonNode.Parse("[1]"), JsonNode.Parse("{\"a\":1}")));
    }

    [Fact]
    public void JsonNodeComparer_HashCode_ConsistentWithEquality()
    {
        using var comparer = new JsonNodeComparer("skip");
        var a = JsonNode.Parse("{\"a\":1,\"b\":[1,2],\"skip\":9,\"s\":\"x\",\"t\":true,\"n\":null}");
        var b = JsonNode.Parse("{\"n\":null,\"t\":true,\"s\":\"x\",\"skip\":10,\"b\":[2,1],\"a\":1.0}");

        Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
        Assert.Equal(0, comparer.GetHashCode(null));
        Assert.NotEqual(comparer.GetHashCode(JsonNode.Parse("{\"a\":1}")), comparer.GetHashCode(JsonNode.Parse("{\"a\":2}")));
    }

    [Fact]
    public void JsonNodeComparer_IgnoredPropertiesCanBeChanged_DisposeClears()
    {
        var comparer = new JsonNodeComparer { IgnoredProperties = new[] { "x" } };

        Assert.True(comparer.Equals(JsonNode.Parse("{\"x\":1}"), JsonNode.Parse("{\"x\":2}")));
        comparer.Equals(JsonNode.Parse("{\"y\":1}"), JsonNode.Parse("{\"y\":2}"));
        comparer.Dispose();
        Assert.Empty(comparer.DifferentProperties);
    }

    // --- JsonArrayComparer ------------------------------------------------------------------------------------------

    [Fact]
    public void JsonArrayComparer_OrderInsensitive_CountMatters()
    {
        using var comparer = new JsonArrayComparer();

        Assert.True(comparer.Equals(JsonNode.Parse("[1,2,3]")!.AsArray(), JsonNode.Parse("[3,1,2]")!.AsArray()));
        Assert.False(comparer.Equals(JsonNode.Parse("[1,2]")!.AsArray(), JsonNode.Parse("[1,2,3]")!.AsArray()));
        Assert.False(comparer.Equals(JsonNode.Parse("[1,1,2]")!.AsArray(), JsonNode.Parse("[1,2,2]")!.AsArray()));
        Assert.True(comparer.Equals(JsonNode.Parse("[{\"a\":1},{\"b\":2}]")!.AsArray(), JsonNode.Parse("[{\"b\":2},{\"a\":1}]")!.AsArray()));
    }

    [Fact]
    public void JsonArrayComparer_NestedArraysAreComparedByValue()
    {
        // nested arrays were compared with object.Equals, i.e. by reference (D-17)
        using var comparer = new JsonArrayComparer();

        Assert.True(comparer.Equals(JsonNode.Parse("[[1,2],[3]]")!.AsArray(), JsonNode.Parse("[[3],[2,1]]")!.AsArray()));
        Assert.False(comparer.Equals(JsonNode.Parse("[[1,2]]")!.AsArray(), JsonNode.Parse("[[1,3]]")!.AsArray()));
    }

    [Fact]
    public void JsonArrayComparer_IgnoredPropertiesNullsAndNullItems()
    {
        using var comparer = new JsonArrayComparer("id");

        Assert.True(comparer.Equals(JsonNode.Parse("[{\"id\":1,\"v\":1}]")!.AsArray(), JsonNode.Parse("[{\"id\":2,\"v\":1}]")!.AsArray()));
        Assert.True(comparer.Equals(null, null));
        Assert.False(comparer.Equals(new JsonArray(), null));
        Assert.False(comparer.Equals(null, new JsonArray()));
        var array = new JsonArray(1);
        Assert.True(comparer.Equals(array, array));
        Assert.True(comparer.Equals(new JsonArray(null, 1), new JsonArray(1, null)));
        Assert.Equal(new[] { "id" }, comparer.IgnoredProperties);
    }

    [Fact]
    public void JsonArrayComparer_HashCode_OrderInsensitive()
    {
        using var comparer = new JsonArrayComparer("id");

        Assert.Equal(
            comparer.GetHashCode(JsonNode.Parse("[1,[2,3],{\"id\":5,\"a\":1}]")!.AsArray()),
            comparer.GetHashCode(JsonNode.Parse("[{\"id\":6,\"a\":1},1,[3,2]]")!.AsArray()));
    }

    // --- JsonEnumConverter ------------------------------------------------------------------------------------------

    public enum Status
    {
        [JsonEnumName("in-progress")]
        InProgress = 1,
        Done = 2,
        [JsonEnumName("x")]
        Other = 3,
    }

    private sealed class StatusHolder
    {
        [JsonConverter(typeof(JsonEnumConverter<Status>))]
        public Status Status { get; set; }
    }

    private static JsonSerializerOptions EnumOptions() => new() { Converters = { new JsonEnumConverter<Status>() } };

    [Fact]
    public void JsonEnumConverter_WritesAttributeNameThenMemberName()
    {
        Assert.Equal("\"in-progress\"", JsonSerializer.Serialize(Status.InProgress, EnumOptions()));
        Assert.Equal("\"Done\"", JsonSerializer.Serialize(Status.Done, EnumOptions()));
    }

    [Fact]
    public void JsonEnumConverter_ReadsAttributeNameMemberNameCaseInsensitiveAndNumbers()
    {
        // it only accepted attribute names although it wrote member names (D-19)
        Assert.Equal(Status.InProgress, JsonSerializer.Deserialize<Status>("\"in-progress\"", EnumOptions()));
        Assert.Equal(Status.Done, JsonSerializer.Deserialize<Status>("\"Done\"", EnumOptions()));
        Assert.Equal(Status.Done, JsonSerializer.Deserialize<Status>("\"DONE\"", EnumOptions()));
        Assert.Equal(Status.Done, JsonSerializer.Deserialize<Status>("2", EnumOptions()));
        Assert.Equal(Status.Done, JsonSerializer.Deserialize<Status>("\"2\"", EnumOptions()));
    }

    [Fact]
    public void JsonEnumConverter_UnknownValue_IsAJsonException_UndefinedNumbersRoundTrip()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Status>("\"nope\"", EnumOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Status>("true", EnumOptions()));
        var undefined = (Status)99;
        var json = JsonSerializer.Serialize(undefined, EnumOptions()); // used to throw a NullReferenceException
        Assert.Equal("99", json);
        Assert.Equal(undefined, JsonSerializer.Deserialize<Status>(json, EnumOptions()));
    }

    [Fact]
    public void JsonEnumConverter_WorksAsPropertyAttribute()
    {
        var json = JsonSerializer.Serialize(new StatusHolder { Status = Status.Other });

        Assert.Equal("{\"Status\":\"x\"}", json);
        Assert.Equal(Status.Other, JsonSerializer.Deserialize<StatusHolder>(json)!.Status);
    }

    [Fact]
    public void JsonEnumNameAttribute_KeepsTheName()
    {
        Assert.Equal("n", new JsonEnumNameAttribute("n").Name);
    }

    // --- TimespanToSecondsConverter ---------------------------------------------------------------------------------

    private static JsonSerializerOptions TimeOptions() => new() { Converters = { new TimespanToSecondsConverter() } };

    [Fact]
    public void RoundTrip()
    {
        var json = JsonSerializer.Serialize(TimeSpan.FromSeconds(90.5), TimeOptions());

        Assert.Equal("90.5", json); // a culture dependent string before (D-18)
        Assert.Equal(TimeSpan.FromSeconds(90.5), JsonSerializer.Deserialize<TimeSpan>(json, TimeOptions()));
    }

    [Fact]
    public void ReadsLegacyString()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("1.5", JsonSerializer.Serialize(TimeSpan.FromSeconds(1.5), TimeOptions()));
            Assert.Equal(TimeSpan.FromSeconds(1.5), JsonSerializer.Deserialize<TimeSpan>("\"1.5\"", TimeOptions()));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TimeSpan>("\"abc\"", TimeOptions()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("1.5", JsonSerializer.Serialize(TimeSpan.FromSeconds(1.5), TimeOptions()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
