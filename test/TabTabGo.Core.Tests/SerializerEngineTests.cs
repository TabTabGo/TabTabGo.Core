using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace TabTabGo.Core.Tests;

public class SerializerEngineTests
{
    public enum Level { Low = 1, High = 5 }

    public class Styled
    {
        public Level Level { get; set; }

        public string? Missing { get; set; }

        public string FullName { get; set; } = string.Empty;
    }

    public class Person
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    // --- options ----------------------------------------------------------------------------------------------------

    [Fact]
    public void JsonSerializationSettings_IsReadOnly_AndTheSameInstance()
    {
        // the options used to be a public mutable lazy singleton (O-10)
        Assert.Same(SerializerEngine.JsonSerializationSettings, SerializerEngine.JsonSerializationSettings);
        Assert.Throws<InvalidOperationException>(() => SerializerEngine.JsonSerializationSettings.WriteIndented = false);
        Assert.Throws<InvalidOperationException>(() => SerializerEngine.JsonSerializationSettings.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
    }

    [Fact]
    public void CreateJsonSerializerOptions_ReturnsIndependentMutableCopies()
    {
        var one = SerializerEngine.CreateJsonSerializerOptions();
        var two = SerializerEngine.CreateJsonSerializerOptions();

        one.PropertyNamingPolicy = null;

        Assert.NotSame(one, two);
        Assert.Equal(JsonNamingPolicy.CamelCase, two.PropertyNamingPolicy);
        Assert.Equal(JsonNamingPolicy.CamelCase, SerializerEngine.JsonSerializationSettings.PropertyNamingPolicy);
    }

    [Fact]
    public void JsonSerializationSettings_CamelCaseEnumsAsStringsAndNullsSkipped()
    {
        var json = SerializerEngine.ObjectToJson(new Styled { Level = Level.High, Missing = null, FullName = "x" });

        Assert.Equal("{\"level\":\"high\",\"fullName\":\"x\"}", json.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty));
    }

    [Fact]
    public async Task JsonSerializationSettings_ConcurrentUse_IsSafe()
    {
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() => SerializerEngine.JsonToObject<Person>("{\"name\":\"p" + i + "\",\"age\":" + i + "}")!.Age));

        Assert.Equal(Enumerable.Range(0, 20), (await Task.WhenAll(tasks)).OrderBy(x => x));
    }

    // --- XML object serialisation -----------------------------------------------------------------------------------

    [Fact]
    public void ObjectToXml_AndXmlToObject_RoundTrip()
    {
        var xml = SerializerEngine.ObjectToXml(new Person { Name = "Sam", Age = 7 });
        var back = SerializerEngine.XmlToObject<Person>(xml);

        Assert.Contains("<Name>Sam</Name>", xml);
        Assert.Equal(7, back!.Age);
        Assert.Throws<ArgumentNullException>(() => SerializerEngine.ObjectToXml(null!));
        Assert.Throws<ArgumentNullException>(() => SerializerEngine.XmlToObject<Person>(null!));
    }

    // --- XML <-> JSON -----------------------------------------------------------------------------------------------

    [Fact]
    public void XmlToJson_String_ScalarsAndNestedObjects()
    {
        var json = JsonNode.Parse(SerializerEngine.XmlToJson("<root><a>1</a><b><c>x</c></b><empty/></root>"))!;

        Assert.Equal("1", json["a"]!.GetValue<string>());
        Assert.Equal("x", json["b"]!["c"]!.GetValue<string>());
        Assert.Equal(string.Empty, json["empty"]!.GetValue<string>());
    }

    [Fact]
    public void XmlToJson_RepeatedElements_BecomeAnArray()
    {
        // used to throw an ArgumentException for the duplicate key (D-16)
        var json = JsonNode.Parse(SerializerEngine.XmlToJson("<root><item>1</item><item>2</item><item>3</item><other>x</other></root>"))!;

        Assert.Equal(new[] { "1", "2", "3" }, json["item"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("x", json["other"]!.GetValue<string>());
    }

    [Fact]
    public void XmlToJson_RepeatedObjects_StayInDocumentOrder()
    {
        var json = JsonNode.Parse(SerializerEngine.XmlToJson("<root><row><id>1</id></row><row><id>2</id></row></root>"))!;

        Assert.Equal(new[] { "1", "2" }, json["row"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()));
    }

    [Fact]
    public void XmlToJson_Attributes_AreKeptAsAtNames()
    {
        var json = JsonNode.Parse(SerializerEngine.XmlToJson("<root id=\"9\"><item code=\"c\">text</item><flag on=\"1\"/></root>"))!;

        Assert.Equal("9", json["@id"]!.GetValue<string>());
        Assert.Equal("c", json["item"]!["@code"]!.GetValue<string>());
        Assert.Equal("text", json["item"]!["#text"]!.GetValue<string>());
        Assert.Equal("1", json["flag"]!["@on"]!.GetValue<string>());
        Assert.Null(json["flag"]!["#text"]);
    }

    [Fact]
    public void XmlToJson_NamespaceDeclarationsAreNotAttributes()
    {
        var json = JsonNode.Parse(SerializerEngine.XmlToJson("<root xmlns:x=\"urn:x\"><x:a>1</x:a></root>"))!;

        Assert.Null(json["@x"]);
        Assert.Equal("1", json["a"]!.GetValue<string>());
    }

    [Fact]
    public void XmlToJson_XmlNode_NoLongerRecursesForever()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<root><a>1</a></root>");

        var json = JsonNode.Parse(SerializerEngine.XmlToJson((XmlNode)doc))!; // infinite recursion before (D-14)

        Assert.Equal("1", json["a"]!.GetValue<string>());
        Assert.Throws<ArgumentNullException>(() => SerializerEngine.XmlToJson((XmlNode)null!));
    }

    [Fact]
    public void XmlToJson_XDocument_NullAndEmptyRoot()
    {
        Assert.Equal("{}", SerializerEngine.XmlToJson((XDocument?)null));
        Assert.Equal("null", SerializerEngine.XmlToJson((XDocument?)null, "null"));
        Assert.Equal("{}", SerializerEngine.XmlToJson(new XDocument()));
        Assert.Equal("1", JsonNode.Parse(SerializerEngine.XmlToJson(XDocument.Parse("<r><a>1</a></r>")))!["a"]!.GetValue<string>());
    }

    [Fact]
    public void XmlToDynamic_Overloads()
    {
        dynamic fromString = SerializerEngine.XmlToDynamic("<r><a>1</a></r>")!;
        dynamic fromDocument = SerializerEngine.XmlToDynamic(XDocument.Parse("<r><a>2</a></r>"))!;
        dynamic empty = SerializerEngine.XmlToDynamic(string.Empty)!;

        Assert.Equal("1", (string)fromString["a"]!);
        Assert.Equal("2", (string)fromDocument["a"]!);
        Assert.Empty((JsonObject)empty);
    }

    [Fact]
    public void JsonToXml_ObjectsArraysAndScalars()
    {
        var xml = SerializerEngine.JsonToXml("{\"a\":1,\"list\":[\"x\",\"y\"],\"o\":{\"b\":true},\"n\":null}", "doc");

        Assert.Equal("doc", xml.Root!.Name.LocalName);
        Assert.Equal("1", xml.Root.Element("a")!.Value);
        Assert.Equal("x", xml.Root.Element("list")!.Element("list0")!.Value);
        Assert.Equal("y", xml.Root.Element("list")!.Element("list1")!.Value);
        Assert.Equal("True", xml.Root.Element("o")!.Element("b")!.Value);
        Assert.Equal(string.Empty, xml.Root.Element("n")!.Value);
    }

    [Fact]
    public void JsonToXml_EmptyInput_ReturnsEmptyRoot()
    {
        Assert.Equal("root", SerializerEngine.JsonToXml(string.Empty).Root!.Name.LocalName);
        Assert.Equal("root", SerializerEngine.JsonToXml(null!).Root!.Name.LocalName);
    }

    [Fact]
    public void JsonToXml_NamesThatAreNotValidXmlNames_AreEncoded()
    {
        // these used to throw an XmlException (D-16)
        var xml = SerializerEngine.JsonToXml("{\"first name\":\"a\",\"1st\":\"b\",\"a/b\":\"c\"}", "my root");

        Assert.Equal("a", xml.Root!.Element(XmlConvert.EncodeLocalName("first name"))!.Value);
        Assert.Equal("b", xml.Root.Element(XmlConvert.EncodeLocalName("1st"))!.Value);
        Assert.Equal(XmlConvert.EncodeLocalName("my root"), xml.Root.Name.LocalName);
        Assert.Equal("a", XmlConvert.DecodeName(xml.Root.Elements().First().Name.LocalName) == "first name" ? "a" : "?");
    }

    [Fact]
    public void JsonToXml_AtNamesBecomeAttributes_HashTextBecomesText()
    {
        var xml = SerializerEngine.JsonToXml("{\"item\":{\"@code\":\"c\",\"#text\":\"body\"}}");

        var item = xml.Root!.Element("item")!;
        Assert.Equal("c", item.Attribute("code")!.Value);
        Assert.Equal("body", item.Value);
    }

    [Fact]
    public void XmlJson_RoundTrip_ObjectWithAttributesAndRepeats()
    {
        var original = "<root id=\"1\"><row code=\"a\">x</row><name>n</name></root>";

        var json = SerializerEngine.XmlToJson(original);
        var xml = SerializerEngine.JsonToXml(json, "root");

        Assert.Equal("1", xml.Root!.Attribute("id")!.Value);
        Assert.Equal("a", xml.Root.Element("row")!.Attribute("code")!.Value);
        Assert.Equal("x", xml.Root.Element("row")!.Value);
        Assert.Equal("n", xml.Root.Element("name")!.Value);
    }

    // --- dynamic / object / json ------------------------------------------------------------------------------------

    [Fact]
    public void DynamicToJson_AndDynamicToXml()
    {
        dynamic value = new JsonObject { ["a"] = 1 };

        Assert.Contains("\"a\": 1", SerializerEngine.DynamicToJson(value));
        Assert.Equal("1", SerializerEngine.DynamicToXml(value).Root!.Element("a")!.Value);
        Assert.Equal("null", SerializerEngine.DynamicToJson(null));
    }

    [Fact]
    public void ObjectToDynamic_ObjectToJson_JsonToObject_JsonToDynamic()
    {
        var person = new Person { Name = "Sam", Age = 3 };

        dynamic dyn = SerializerEngine.ObjectToDynamic(person)!;
        var json = SerializerEngine.ObjectToJson(person);
        var back = SerializerEngine.JsonToObject<Person>(json);

        Assert.Equal("Sam", (string)dyn["name"]!);
        Assert.Equal(3, back!.Age);
        Assert.Equal(7, (int)SerializerEngine.JsonToDynamic("{\"a\":7}")!["a"]!);
        Assert.Empty((JsonObject)SerializerEngine.JsonToDynamic("null")!);
        Assert.Empty((JsonObject)SerializerEngine.JsonToDynamic(string.Empty)!);
    }

    // --- enums ------------------------------------------------------------------------------------------------------

    [Fact]
    public void EnumToJson_Type_ReturnsIdNameArray()
    {
        // used to throw RuntimeBinderException (D-15)
        var json = JsonNode.Parse(SerializerEngine.EnumToJson(typeof(Level)))!.AsArray();

        Assert.Equal(2, json.Count);
        Assert.Equal(1, json[0]!["id"]!.GetValue<int>());
        Assert.Equal("Low", json[0]!["name"]!.GetValue<string>());
        Assert.Equal(5, json[1]!["id"]!.GetValue<int>());
    }

    [Fact]
    public void EnumToDynamic_TypeAndValue()
    {
        var all = (JsonArray)SerializerEngine.EnumToDynamic(typeof(Level));
        var one = (JsonObject)SerializerEngine.EnumToDynamic(Level.High);

        Assert.Equal(2, all.Count);
        Assert.Equal("High", one["name"]!.GetValue<string>());
        Assert.Equal(5, one["id"]!.GetValue<int>());
        Assert.Contains("\"id\": 5", SerializerEngine.EnumToJson(Level.High));
        Assert.Throws<ArgumentNullException>(() => SerializerEngine.EnumToDynamic((Type)null!));
        Assert.Throws<ArgumentNullException>(() => SerializerEngine.EnumToDynamic((object)null!));
    }

    [Fact]
    public void EnumToDynamic_LongBackedEnums_DoNotOverflow()
    {
        var one = (JsonObject)SerializerEngine.EnumToDynamic(LongEnum.Big);

        Assert.Equal(5_000_000_000L, one["id"]!.GetValue<long>());
    }

    public enum LongEnum : long { Big = 5_000_000_000L }

    // --- dictionary <-> json ------------------------------------------------------------------------------------------

    [Fact]
    public void ConvertDictionaryToJson_BuildsObjectsAndArrays()
    {
        var json = SerializerEngine.ConvertDictionaryToJson(new Dictionary<string, string?>
        {
            ["prop1"] = "val1",
            ["obj1_subprop1"] = "subval1",
            ["obj1_subprop2"] = "subval2",
            ["arr1_0_item"] = "arrayval1",
            ["arr1_1_item"] = "arrayval2",
            ["prop2"] = "val2",
        });

        Assert.Equal("subval1", json["obj1"]!["subprop1"]!.GetValue<string>());
        Assert.Equal("arrayval2", json["arr1"]![1]!["item"]!.GetValue<string>());
        Assert.Equal("val2", json["prop2"]!.GetValue<string>());
    }

    [Fact]
    public void ConvertDictionaryToJson_ScalarTypesAndSeparators()
    {
        var json = SerializerEngine.ConvertDictionaryToJson(new Dictionary<string, string?>
        {
            ["i"] = "42", ["d"] = "1.5", ["b"] = "true", ["s"] = "text", ["n"] = null, ["neg"] = "-7", ["nan"] = "NaN", ["a.b"] = "dot",
        });

        Assert.Equal(42L, json["i"]!.GetValue<long>());
        Assert.Equal(1.5, json["d"]!.GetValue<double>());
        Assert.True(json["b"]!.GetValue<bool>());
        Assert.Equal("text", json["s"]!.GetValue<string>());
        Assert.Equal(string.Empty, json["n"]!.GetValue<string>());
        Assert.Equal(-7L, json["neg"]!.GetValue<long>());
        Assert.Equal("NaN", json["nan"]!.GetValue<string>()); // NaN has no JSON number form
        Assert.Equal("dot", json["a"]!["b"]!.GetValue<string>());

        var custom = SerializerEngine.ConvertDictionaryToJson(new Dictionary<string, string?> { ["x-y"] = "1" }, new[] { '-' });
        Assert.Equal(1L, custom["x"]!["y"]!.GetValue<long>());
        Assert.Throws<ArgumentNullException>(() => SerializerEngine.ConvertDictionaryToJson(null!));
    }

    [Fact]
    public void MapToJsonObject_RepeatedKey_OverwritesInsteadOfThrowing()
    {
        var json = new JsonObject();

        SerializerEngine.MapToJsonObject(new[] { "a" }, json, "1");
        SerializerEngine.MapToJsonObject(new[] { "a" }, json, "2");

        Assert.Equal(2L, json["a"]!.GetValue<long>());
    }

    [Fact]
    public void MapToJsonObject_SparseArrayIndex_IsPadded()
    {
        var json = new JsonObject();

        SerializerEngine.MapToJsonObject(new[] { "arr", "2", "item" }, json, "x");

        var array = json["arr"]!.AsArray();
        Assert.Equal(3, array.Count);
        Assert.Null(array[0]);
        Assert.Null(array[1]);
        Assert.Equal("x", array[2]!["item"]!.GetValue<string>());
    }

    [Fact]
    public void MapToJsonObject_PrimitiveArrayItems()
    {
        var json = new JsonObject();

        SerializerEngine.MapToJsonObject(new[] { "tags", "0" }, json, "a");
        SerializerEngine.MapToJsonObject(new[] { "tags", "1" }, json, "b");

        Assert.Equal(new[] { "a", "b" }, json["tags"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void MapToJsonObject_ReusesExistingChildren_AndIsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var json = new JsonObject();
            SerializerEngine.MapToJsonObject(new[] { "o", "a" }, json, "1.5");
            SerializerEngine.MapToJsonObject(new[] { "o", "b" }, json, "2");

            Assert.Equal(1.5, json["o"]!["a"]!.GetValue<double>()); // "1.5" is 15 in de-DE
            Assert.Equal(2L, json["o"]!["b"]!.GetValue<long>());
            Assert.Throws<ArgumentNullException>(() => SerializerEngine.MapToJsonObject(null!, json, "x"));
            Assert.Throws<ArgumentNullException>(() => SerializerEngine.MapToJsonObject(new[] { "a" }, null!, "x"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ConvertJsonToDictionary_Scalars_Present()
    {
        // every scalar used to be dropped because the runtime type check never matched (O-10)
        var json = JsonNode.Parse("{\"s\":\"x\",\"i\":1,\"b\":true,\"d\":1.5,\"n\":null}")!.AsObject();

        var dictionary = SerializerEngine.ConvertJsonToDictionary(json);

        Assert.Equal("x", dictionary["s"]);
        Assert.Equal("1", dictionary["i"]);
        Assert.Equal("true", dictionary["b"]);
        Assert.Equal("1.5", dictionary["d"]);
        Assert.Equal(string.Empty, dictionary["n"]); // null used to throw
    }

    [Fact]
    public void ConvertJsonToDictionary_NestedObjectsArraysAndSeparator()
    {
        var json = JsonNode.Parse("{\"o\":{\"p\":1},\"arr\":[{\"item\":\"a\"},{\"item\":\"b\"}],\"tags\":[\"x\",\"y\"]}")!.AsObject();

        var underscore = SerializerEngine.ConvertJsonToDictionary(json);
        var dotted = SerializerEngine.ConvertJsonToDictionary(json, ".");

        Assert.Equal("1", underscore["o_p"]);
        Assert.Equal("b", underscore["arr_1_item"]);
        Assert.Equal("y", underscore["tags_1"]); // arrays of scalars used to be cast to JsonObject and crash
        Assert.Equal("a", dotted["arr.0.item"]);
        Assert.Empty(SerializerEngine.ConvertJsonToDictionary(null));
    }

    [Fact]
    public void ConvertJsonToDictionary_ThenBack_RoundTrips()
    {
        var json = JsonNode.Parse("{\"a\":{\"b\":[{\"c\":\"1\"},{\"c\":\"2\"}]},\"z\":\"last\"}")!.AsObject();

        var back = SerializerEngine.ConvertDictionaryToJson(SerializerEngine.ConvertJsonToDictionary(json).ToDictionary(p => p.Key, p => (string?)p.Value));

        Assert.Equal(1L, back["a"]!["b"]![0]!["c"]!.GetValue<long>());
        Assert.Equal("last", back["z"]!.GetValue<string>());
    }
}
