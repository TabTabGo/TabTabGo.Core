using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Extensions;

namespace TabTabGo.Core.Tests;

public class ExtensionsTests
{
    public class Address
    {
        public string City { get; set; } = string.Empty;

        public string? Street { get; set; }
    }

    public class Person : Entity
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }

        public int? Rank { get; set; }

        public Address Address { get; set; } = new();

        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("e_mail")]
        public string? Email { get; set; }

        [JsonIgnore]
        public string Secret { get; set; } = "kept";

        public string ReadOnly { get; } = "ro";
    }

    // --- JsonExtensions ---------------------------------------------------------------------------------------------

    [Fact]
    public void Populate_PartialJson_KeepsOtherFields()
    {
        // every property missing from the patch used to be overwritten with null/default (D-13)
        var person = new Person { Name = "Sam", Age = 40, Email = "a@b.c", Address = { City = "Dubai", Street = "Main" } };

        JsonNode.Parse("{\"age\":41}")!.Populate(person);

        Assert.Equal(41, person.Age);
        Assert.Equal("Sam", person.Name);
        Assert.Equal("a@b.c", person.Email);
        Assert.Equal("Dubai", person.Address.City);
    }

    [Fact]
    public void Populate_CamelCase_Matches()
    {
        var person = new Person();

        JsonNode.Parse("{\"name\":\"Sam\",\"NAME2\":\"ignored\",\"Age\":3}")!.Populate(person);

        Assert.Equal("Sam", person.Name);
        Assert.Equal(3, person.Age);
    }

    [Fact]
    public void Populate_Nested_Merges()
    {
        var person = new Person { Address = { City = "Dubai", Street = "Main" } };

        JsonNode.Parse("{\"address\":{\"street\":\"Side\"}}")!.Populate(person);

        Assert.Equal("Dubai", person.Address.City);
        Assert.Equal("Side", person.Address.Street);
    }

    [Fact]
    public void Populate_Unknown_ToExtraProperties()
    {
        var person = new Person();

        JsonNode.Parse("{\"color\":\"red\",\"n\":5,\"nested\":{\"a\":1},\"nothing\":null}")!.Populate(person);

        Assert.Equal("red", person.ExtraProperties["color"]);
        Assert.Equal(5L, person.ExtraProperties["n"]);
        Assert.Equal(1L, ((Dictionary<string, object?>)person.ExtraProperties["nested"]!)["a"]);
        Assert.Null(person.ExtraProperties["nothing"]);
    }

    [Fact]
    public void Populate_JsonNull_ClearsNullableAndReferenceButNotValueTypes()
    {
        var person = new Person { Rank = 3, Email = "x", Age = 9 };

        JsonNode.Parse("{\"rank\":null,\"e_mail\":null,\"age\":null}")!.Populate(person);

        Assert.Null(person.Rank);
        Assert.Null(person.Email);
        Assert.Equal(9, person.Age);
    }

    [Fact]
    public void Populate_JsonPropertyNameAndIgnoredAndReadOnly()
    {
        var person = new Person();

        JsonNode.Parse("{\"e_mail\":\"m@x\",\"secret\":\"hacked\",\"readOnly\":\"x\"}")!.Populate(person);

        Assert.Equal("m@x", person.Email);
        Assert.Equal("kept", person.Secret); // [JsonIgnore] properties cannot be set from a patch
        Assert.Equal("ro", person.ReadOnly);
    }

    [Fact]
    public void Populate_ArrayReplacesCollection_NonObjectThrows_NullsThrow()
    {
        var person = new Person { Tags = { "old" } };

        JsonNode.Parse("{\"tags\":[\"a\",\"b\"]}")!.Populate(person);

        Assert.Equal(new[] { "a", "b" }, person.Tags);
        Assert.Throws<ArgumentException>(() => JsonNode.Parse("[1]")!.Populate(person));
        Assert.Throws<ArgumentNullException>(() => ((JsonNode)null!).Populate(person));
        Assert.Throws<ArgumentNullException>(() => JsonNode.Parse("{}")!.Populate<Person>(null!));
    }

    [Fact]
    public void Populate_NonEntityTarget_UnknownPropertiesAreIgnored()
    {
        var address = new Address { City = "A" };

        JsonNode.Parse("{\"city\":\"B\",\"zip\":\"1\"}")!.Populate(address);

        Assert.Equal("B", address.City);
    }

    [Fact]
    public void ConvertToXml_StringJsonNodeAndArrays()
    {
        Assert.Contains("<a>1</a>", "{\"a\":1}".ConvertToXml());
        Assert.Contains("<doc>", "{\"a\":1}".ConvertToXml("doc"));
        Assert.Contains("<a>1</a>", JsonNode.Parse("{\"a\":1}")!.ConvertToXml());
        Assert.Contains("<items>", JsonNode.Parse("[1,2]")!.ConvertToXml("items"));
    }

    [Fact]
    public void ConvertToDictionary_PlainAndPathKeys()
    {
        var json = JsonNode.Parse("{\"a\":1,\"o\":{\"b\":\"x\"}}")!.AsObject();

        var plain = json.ConvertToDictionary();
        var paths = json.ConvertToDictionary(true);
        var custom = json.ConvertToDictionary(true, ".");

        Assert.Equal(2, plain.Count);
        Assert.Equal("x", paths["o_b"]); // returned null before (N-21)
        Assert.Equal("x", custom["o.b"]);
        Assert.Empty(((JsonObject?)null).ConvertToDictionary());
        Assert.Empty(((JsonObject?)null).ConvertToDictionary(true));
    }

    [Fact]
    public void SerializeAndJsonOptions()
    {
        Assert.Same(SerializerEngine.JsonSerializationSettings, JsonExtensions.JsonOptions);
        Assert.Contains("\"a\": 1", new JsonObject { ["a"] = 1 }.Serialize());
        Assert.Contains("\"fullName\": \"x\"", new SerializerEngineTests.Styled { FullName = "x" }.Serialize());
    }

    // --- StringExtensions ---------------------------------------------------------------------------------------------

    [Fact]
    public void StringExtensions_FirstCharToUpper()
    {
        Assert.Equal("Name", "name".FirstCharToUpper());
        Assert.Equal("N", "n".FirstCharToUpper());
        Assert.Equal("I", "i".FirstCharToUpper()); // invariant: no Turkish dotted i
        Assert.Throws<ArgumentNullException>(() => ((string)null!).FirstCharToUpper());
        Assert.Throws<ArgumentException>(() => string.Empty.FirstCharToUpper());
    }

    [Fact]
    public void StringExtensions_Deserialise()
    {
        Assert.Equal(1, "{\"a\":1}".DeserializeToJson()!["a"]!.GetValue<int>());
        Assert.Null("[1]".DeserializeToJson());
        Assert.Equal(2, "[1,2]".DeserializeToJArray()!.Count);
        Assert.Null("{}".DeserializeToJArray());
        Assert.Equal(3, "{\"age\":3}".Deserialize<Person>()!.Age);
    }

    // --- XmlExtensions ---------------------------------------------------------------------------------------------

    [Fact]
    public void XmlExtensions_ConvertToJson_AllInputs()
    {
        // an empty string used to fall through to the parser and throw (N-22)
        Assert.Empty("".ConvertToJson()!);
        Assert.Equal("1", "<r><a>1</a></r>".ConvertToJson()!["a"]!.GetValue<string>());
        var xmlDocument = new XmlDocument();
        xmlDocument.LoadXml("<r><a>2</a></r>");
        Assert.Equal("2", xmlDocument.ConvertToJson()["a"]!.GetValue<string>());
        Assert.Empty(((XmlDocument?)null).ConvertToJson());
        Assert.Equal("3", XDocument.Parse("<r><a>3</a></r>").ConvertToJson()!["a"]!.GetValue<string>());
        Assert.Empty(((XDocument?)null).ConvertToJson()!);
    }

    [Fact]
    public void XmlExtensions_ConvertToJsonArray()
    {
        Assert.Empty("<r><a>1</a></r>".ConvertToJsonArray());
        Assert.Equal(2, "<r><item>1</item><item>2</item></r>".ConvertToJsonArray("item").Count);
        Assert.Empty("<r><item>1</item></r>".ConvertToJsonArray("item")); // a single element is not an array
        Assert.Empty("<r/>".ConvertToJsonArray());
    }

    [Fact]
    public void XmlExtensions_ConvertToDictionary_AllInputs()
    {
        var xmlDocument = new XmlDocument();
        xmlDocument.LoadXml("<r><o><b>x</b></o></r>");

        Assert.Equal("x", "<r><o><b>x</b></o></r>".ConvertToDictionary(true)["o_b"]);
        Assert.Equal("x", XDocument.Parse("<r><o><b>x</b></o></r>").ConvertToDictionary(true, ".")["o.b"]);
        Assert.Single(xmlDocument.ConvertToDictionary());
    }

    // --- DictionaryExtensions -------------------------------------------------------------------------------------------

    [Fact]
    public void DictionaryEqual_SameDifferentAndNulls()
    {
        var a = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 };
        var b = new Dictionary<string, int> { ["y"] = 2, ["x"] = 1 };

        Assert.True(a.DictionaryEqual(b));
        Assert.True(a.DictionaryEqual(a));
        Assert.True(((Dictionary<string, int>?)null).DictionaryEqual(null));
        Assert.False(a.DictionaryEqual(null));
        Assert.False(((Dictionary<string, int>?)null).DictionaryEqual(a));
        Assert.False(a.DictionaryEqual(new Dictionary<string, int> { ["x"] = 1 }));
        Assert.False(a.DictionaryEqual(new Dictionary<string, int> { ["x"] = 1, ["z"] = 2 }));
        Assert.False(a.DictionaryEqual(new Dictionary<string, int> { ["x"] = 1, ["y"] = 3 }));
    }

    [Fact]
    public void DictionaryEqual_CustomValueComparer()
    {
        var a = new Dictionary<string, string> { ["x"] = "A" };
        var b = new Dictionary<string, string> { ["x"] = "a" };

        Assert.False(a.DictionaryEqual(b));
        Assert.True(a.DictionaryEqual(b, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddOrUpdate_AddsAndReplaces()
    {
        var dictionary = new Dictionary<string, int>();

        dictionary.AddOrUpdate("a", 1);
        dictionary.AddOrUpdate("a", 2);

        Assert.Equal(2, dictionary["a"]);
    }

    [Fact]
    public void Dictionary_ConvertToJsonAndXml()
    {
        var dictionary = new Dictionary<string, object?> { ["a"] = 1, ["o_b"] = "x" };

        Assert.Equal(1, dictionary.ConvertToJson()!["a"]!.GetValue<int>());
        Assert.Equal("x", dictionary.ConvertToJson(true)!["o"]!["b"]!.GetValue<string>());
        Assert.Equal("x", dictionary.ConvertToJson(true, new[] { '_' })!["o"]!["b"]!.GetValue<string>());
        Assert.Empty(((IDictionary<string, object?>?)null).ConvertToJson()!.AsObject());
        Assert.Contains("<a>1</a>", dictionary.ConvertToXml());
        Assert.Equal(string.Empty, ((IDictionary<string, object?>?)null).ConvertToXml());
        Assert.Contains("<b>x</b>", dictionary.ConvertToXml(true));
        Assert.Equal(string.Empty, new Dictionary<string, object?> { ["k"] = null }.ConvertToJson(true)!["k"]!.GetValue<string>());
    }

    // --- EntityExtensions ---------------------------------------------------------------------------------------------

    [Fact]
    public void CopyObject_CopiesSettableProperties_SkipsIgnoredAndAudit()
    {
        var source = new Person { Name = "Src", Age = 5, Email = "e", CreatedBy = "creator", Tags = { "t" } };
        var target = new Person { Name = "Dst", CreatedBy = "keep", Tags = { "orig" } };

        source.CopyObject(typeof(Person), target, nameof(Person.Age));

        Assert.Equal("Src", target.Name);
        Assert.Equal(0, target.Age); // ignored by name; used to be copied (N-18)
        Assert.Equal("e", target.Email);
        Assert.Equal("keep", target.CreatedBy); // [IgnoreCopy]
        Assert.Equal(new[] { "t" }, target.Tags); // only array / IEnumerable<T> / ICollection<T> typed properties are skipped; a List<T> is copied by reference
        Assert.Equal("ro", target.ReadOnly);
    }

    [Fact]
    public void CopyObject_Generic_CopiesAndCreatesDestinationWhenMissing()
    {
        var source = new Person { Name = "S" };
        var target = new Person();

        source.CopyObject(target);
        source.CopyObject<Person>(null);

        Assert.Equal("S", target.Name);
    }

    [Fact]
    public void CopyObject_NestedEntity_IsMergedIntoTheExistingOne()
    {
        var source = new Holder { Inner = new Person { Name = "new" } };
        var target = new Holder { Inner = new Person { Name = "old", Age = 8 } };
        var targetInner = target.Inner;

        source.CopyObject(typeof(Holder), target);

        Assert.Same(targetInner, target.Inner);
        Assert.Equal("new", target.Inner.Name);
    }

    [Fact]
    public void CopyObject_NestedEntityWithoutDestination_SharesTheReference()
    {
        var source = new Holder { Inner = new Person { Name = "new" } };
        var target = new Holder();

        source.CopyObject(typeof(Holder), target);

        Assert.Same(source.Inner, target.Inner);
        Assert.Throws<ArgumentNullException>(() => source.CopyObject(typeof(Holder), null!));
    }

    public class Parent : Entity
    {
        public Person? Child { get; set; }
    }

    public class Holder
    {
        public Person? Inner { get; set; }

        [IgnoreCopy]
        public Person? Ignored { get; set; }
    }

    [Fact]
    public void Clone_ReturnsANewInstanceWithTheValues()
    {
        var source = new Person { Name = "Sam", Age = 4, CreatedBy = "me", Address = { City = "X" } };

        var clone = source.Clone();

        Assert.NotSame(source, clone); // returned the source itself before (N-18)
        Assert.Equal("Sam", clone.Name);
        Assert.Equal(4, clone.Age);
        Assert.Equal("me", clone.CreatedBy); // Clone does not apply [IgnoreCopy]
        Assert.Throws<ArgumentNullException>(() => ((Person)null!).Clone());
    }

    [Fact]
    public void IgnoreCopyAttribute_Defaults()
    {
        Assert.True(new IgnoreCopyAttribute().IgnoreCopy);
        Assert.False(new IgnoreCopyAttribute(false).IgnoreCopy);
    }

    // --- collections ----------------------------------------------------------------------------------------------------

    [Fact]
    public void CollectionExtensions_ForeachAndAddRange()
    {
        ICollection<int> collection = new List<int> { 1, 2 };
        var seen = new List<int>();

        collection.Foreach(seen.Add);
        var doubled = collection.Foreach(i => i * 2);
        collection.AddRange(new[] { 3, 4 });

        Assert.Equal(new[] { 1, 2 }, seen);
        Assert.Equal(new[] { 2, 4 }, doubled);
        Assert.Equal(4, collection.Count);
    }

    [Fact]
    public void EnumerableExtensions_ForeachRunsImmediately()
    {
        IEnumerable<int> items = new[] { 1, 2 };
        var seen = new List<int>();

        items.Foreach(seen.Add);
        var evaluated = items.Foreach(i =>
        {
            seen.Add(i * 10);
            return i;
        });

        Assert.Equal(new[] { 1, 2, 10, 20 }, seen); // not lazy: the lambda already ran
        Assert.Equal(2, evaluated.Count());
    }

    [Theory]
    [InlineData(ExportType.Csv, ".csv")]
    [InlineData(ExportType.Excel, ".xlsx")]
    [InlineData(ExportType.Xml, ".xml")]
    [InlineData(ExportType.Json, ".json")]
    [InlineData(ExportType.Html, ".html")]
    [InlineData(ExportType.Yaml, ".csv")]
    [InlineData(ExportType.Pdf, ".csv")]
    public void ExportType_FileExtenstion(ExportType type, string expected)
    {
        Assert.Equal(expected, type.FileExtenstion());
    }

    [Fact]
    public void PropertyAccessorCache_ReturnsMemberAccessLambdas()
    {
        var lambda = PropertyAccessorCache<Person>.Get(nameof(Person.Name))!;

        Assert.Equal("Sam", ((Func<Person, string>)lambda.Compile())(new Person { Name = "Sam" }));
        Assert.Null(PropertyAccessorCache<Person>.Get("Nope"));
        Assert.Same(lambda, PropertyAccessorCache<Person>.Get(nameof(Person.Name)));
    }

    [Fact]
    public void PropertyAccessorCache_SkipsIndexers()
    {
        Assert.Null(PropertyAccessorCache<List<int>>.Get("Item"));
        Assert.NotNull(PropertyAccessorCache<List<int>>.Get("Count"));
    }

    // --- AssemblyExtensions -------------------------------------------------------------------------------------------

    [Fact]
    public void ExtractResources_WritesMatchingResources_AndSkipsFresherFiles()
    {
        var country = typeof(TabTabGo.Core.Country.Services.CountryService).Assembly;
        var folder = Path.Combine(Path.GetTempPath(), "ttg-res-" + Guid.NewGuid().ToString("N"));
        try
        {
            country.ExtractResources("TabTabGo.Core.Country.Resources.", folder);

            var file = Path.Combine(folder, "ar-AE.json");
            Assert.True(File.Exists(file));
            Assert.Contains("\"AE\"", File.ReadAllText(file));
            Assert.False(File.Exists(Path.Combine(folder, "countries_info.json"))); // other prefix

            File.WriteAllText(file, "newer");
            File.SetLastWriteTime(file, DateTime.Now.AddDays(1));
            country.ExtractResources("TabTabGo.Core.Country.Resources.", folder);
            Assert.Equal("newer", File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ExtractResources_GzipResourcesAreDecompressed_AndArgumentsChecked()
    {
        var assembly = BuildAssemblyWithGzipResource(out var folder);
        try
        {
            assembly.ExtractResources("Gz.", folder);

            Assert.Equal("zipped text", File.ReadAllText(Path.Combine(folder, "note.txt")));
            Assert.Throws<ArgumentNullException>(() => ((System.Reflection.Assembly)null!).ExtractResources("x", folder));
            Assert.Throws<ArgumentNullException>(() => assembly.ExtractResources(null!, folder));
            Assert.Throws<ArgumentException>(() => assembly.ExtractResources("x", ""));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static System.Reflection.Assembly BuildAssemblyWithGzipResource(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "ttg-gz-" + Guid.NewGuid().ToString("N"));
        return new GzipAssembly();
    }

    private sealed class GzipAssembly : System.Reflection.Assembly
    {
        public override string[] GetManifestResourceNames() => new[] { "Gz.note.txt.gz", "Other.x.txt" };

        public override Stream? GetManifestResourceStream(string name)
        {
            if (name != "Gz.note.txt.gz")
            {
                return null;
            }

            var buffer = new MemoryStream();
            using (var zip = new System.IO.Compression.GZipStream(buffer, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
            {
                zip.Write(System.Text.Encoding.UTF8.GetBytes("zipped text"));
            }

            buffer.Position = 0;
            return buffer;
        }

        public override string Location => string.Empty;
    }

    // --- Entity ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Entity_Defaults()
    {
        var entity = new Entity();

        Assert.True(entity.IsEnabled);
        Assert.Empty(entity.ExtraProperties);
        Assert.InRange(entity.CreatedDate, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Null(entity.CreatedBy);
        Assert.Null(entity.UpdatedBy);
    }

    [Fact]
    public void Entity_ExtraProperties_AreFlattenedByBothEngines()
    {
        var entity = new Person { Name = "n" };
        entity.ExtraProperties["color"] = "red";

        var stj = JsonSerializer.Serialize(entity, SerializerEngine.JsonSerializationSettings);
        var newtonsoft = Newtonsoft.Json.JsonConvert.SerializeObject(entity);

        Assert.Equal("red", JsonNode.Parse(stj)!["color"]!.GetValue<string>());
        Assert.Equal("red", (string?)Newtonsoft.Json.Linq.JObject.Parse(newtonsoft)["color"]);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<Person>("{\"Name\":\"n\",\"extra\":5}")!;
        Assert.Equal(5L, back.ExtraProperties["extra"]);
        var backStj = JsonSerializer.Deserialize<Person>("{\"name\":\"n\",\"extra\":5}", SerializerEngine.JsonSerializationSettings)!;
        Assert.Equal(5, ((JsonElement)backStj.ExtraProperties["extra"]!).GetInt32());
    }

    [Fact]
    public void CultureIndependence_Sanity()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Item", "item".FirstCharToUpper());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Populate_ProtectedEntityProperties_AreSkipped()
    {
        var parent = new Parent { Child = new Person { Name = "keep" } };
        var person = new Person { Name = "n", IsEnabled = true, CreatedBy = "creator" };
        var before = person.CreatedDate;

        JsonNode.Parse("{\"id\":5,\"isEnabled\":false,\"createdBy\":\"me\",\"createdDate\":\"2001-01-01T00:00:00Z\",\"name\":\"changed\"}")!.Populate(person);
        JsonNode.Parse("{\"child\":{\"name\":\"hijack\"}}")!.Populate(parent);

        Assert.Equal("changed", person.Name);
        Assert.True(person.IsEnabled);
        Assert.Equal("creator", person.CreatedBy);
        Assert.Equal(before, person.CreatedDate);
        Assert.Equal("keep", parent.Child!.Name); // a related entity is not merged into through its parent
    }

    [Fact]
    public void Populate_NonEntityTarget_KeepsIdAndNestedMerge()
    {
        var address = new Address { City = "A" };

        JsonNode.Parse("{\"city\":\"B\"}")!.Populate(address);

        Assert.Equal("B", address.City);
    }

    [Fact]
    public void Populate_ExtraProperties_AreBounded()
    {
        var person = new Person();
        var tooMany = new JsonObject();
        for (var i = 0; i < 101; i++)
        {
            tooMany["k" + i] = i;
        }

        Assert.Throws<TabTabGo.Core.Exceptions.ApiException>(() => tooMany.Populate(person));
        Assert.Throws<TabTabGo.Core.Exceptions.ApiException>(() => new JsonObject { [new string('k', 101)] = 1 }.Populate(new Person()));
        Assert.Throws<TabTabGo.Core.Exceptions.ApiException>(() => new JsonObject { ["big"] = new string('x', 20_000) }.Populate(new Person()));
        Assert.Throws<TabTabGo.Core.Exceptions.ApiException>(() => new JsonObject { ["bigObject"] = new JsonObject { ["a"] = new string('x', 20_000) } }.Populate(new Person()));
        var other = new Person();
        new JsonObject { ["fine"] = "ok" }.Populate(other);
        Assert.Equal("ok", other.ExtraProperties["fine"]);
    }

    [Fact]
    public void EntityPropertyGuard_ProtectedSet()
    {
        Assert.True(EntityPropertyGuard.IsProtected(typeof(Person), "id"));
        Assert.True(EntityPropertyGuard.IsProtected(typeof(Person), "PersonId"));
        Assert.True(EntityPropertyGuard.IsProtected(typeof(Person), "CREATEDBY"));
        Assert.True(EntityPropertyGuard.IsProtected(typeof(Parent), "Child"));
        Assert.False(EntityPropertyGuard.IsProtected(typeof(Person), "Name"));
        Assert.False(EntityPropertyGuard.IsProtected(typeof(Person), ""));
        Assert.Contains("IsEnabled", EntityPropertyGuard.ProtectedProperties(typeof(Person)));
        Assert.Throws<ArgumentNullException>(() => EntityPropertyGuard.IsProtected(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => EntityPropertyGuard.ProtectedProperties(null!));
        Assert.Throws<ArgumentNullException>(() => EntityPropertyGuard.EnsureExtraPropertyAllowed(null!, "k", 1));
    }

    // --- I4: properties the serializers or the mapping hide are not writable by a patch ---

    private sealed class Hidden : Entity
    {
        public string Visible { get; set; } = "v";

        [System.Text.Json.Serialization.JsonIgnore]
        public string ByStj { get; set; } = "stj";

        [Newtonsoft.Json.JsonIgnore]
        public string ByNewtonsoft { get; set; } = "nj";

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string ByNotMapped { get; set; } = "nm";

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? WhenNull { get; set; }
    }

    [Theory]
    [InlineData("ByStj", true)]
    [InlineData("byNewtonsoft", true)]
    [InlineData("BYNOTMAPPED", true)]
    [InlineData("Visible", false)]
    [InlineData("WhenNull", false)] // only "ignore always" hides a property
    [InlineData("ExtraProperties", false)]
    public void IsProtected_HiddenProperties(string name, bool expected)
    {
        Assert.Equal(expected, EntityPropertyGuard.IsProtected(typeof(Hidden), name));
    }

    private sealed class PlainWithNewtonsoftIgnore
    {
        public string Visible { get; set; } = "v";

        [Newtonsoft.Json.JsonIgnore]
        public string Hidden { get; set; } = "h";
    }

    [Fact]
    public void Populate_NewtonsoftJsonIgnoreOnANonEntity_IsHonoured()
    {
        var target = new PlainWithNewtonsoftIgnore();

        JsonNode.Parse("{\"visible\":\"changed\",\"hidden\":\"x\"}")!.Populate(target);

        Assert.Equal("changed", target.Visible);
        Assert.Equal("h", target.Hidden);
    }

    [Fact]
    public void Populate_NewtonsoftJsonIgnore_IsHonoured()
    {
        var entity = new Hidden();

        JsonNode.Parse("{\"visible\":\"changed\",\"byNewtonsoft\":\"x\",\"byStj\":\"x\"}")!.Populate(entity);

        Assert.Equal("changed", entity.Visible);
        Assert.Equal("nj", entity.ByNewtonsoft);
        Assert.Equal("stj", entity.ByStj);
    }
}
