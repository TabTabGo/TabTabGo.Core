using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Json;
using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Tests;

public class JsonDefaultsAndConvertersTests
{
    private sealed class Holder
    {
        public JObject? Obj { get; set; }

        public JToken? Tok { get; set; }

        public JArray? Arr { get; set; }

        public JValue? Val { get; set; }
    }

    private sealed class NodeHolder
    {
        public JsonObject? Obj { get; set; }

        public JsonArray? Arr { get; set; }

        public JsonNode? Node { get; set; }

        public JsonValue? Val { get; set; }
    }

    private sealed class ElementHolder
    {
        public JsonElement El { get; set; }

        public JsonElement? MaybeEl { get; set; }

        public JsonDocument? Doc { get; set; }
    }

    private enum Color { RedColor, DarkBlue }

    private sealed class EnumHolder
    {
        public Color Color { get; set; }

        public string? Missing { get; set; }
    }

    // --- JsonDefaults ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateSystemTextJsonOptions_IsAMutableCopy_ThatKeepsTheSharedOnesReadOnly()
    {
        var copy = JsonDefaults.CreateSystemTextJsonOptions();

        copy.WriteIndented = false; // would throw on the shared, read-only instance
        Assert.True(SerializerEngine.JsonSerializationSettings.WriteIndented);
        Assert.Equal(JsonNamingPolicy.CamelCase, copy.PropertyNamingPolicy);
        Assert.Contains(copy.Converters, c => c is JTokenSystemTextJsonConverterFactory);
    }

    [Fact]
    public void CreateNewtonsoftSettings_MirrorsTheSystemTextJsonDefaults()
    {
        var settings = JsonDefaults.CreateNewtonsoftSettings();
        var json = Nj.JsonConvert.SerializeObject(new EnumHolder { Color = Color.DarkBlue }, settings);

        Assert.Equal("{\"color\":\"darkBlue\"}", json); // camelCase names, camelCase string enums, nulls ignored
        Assert.Equal(Nj.DateParseHandling.None, settings.DateParseHandling);
        Assert.Equal("2020-01-01T00:00:00Z", Nj.JsonConvert.DeserializeObject<Dictionary<string, object>>("{\"d\":\"2020-01-01T00:00:00Z\"}", settings)!["d"]);
        Assert.Contains(settings.Converters, c => c is JsonNodeNewtonsoftConverter);
        Assert.Contains(settings.Converters, c => c is JsonElementNewtonsoftConverter);
    }

    [Fact]
    public void AddTabTabGoConverters_IsIdempotent_ForBothEngines()
    {
        var options = new JsonSerializerOptions();
        var settings = new Nj.JsonSerializerSettings();

        options.AddTabTabGoConverters().AddTabTabGoConverters();
        settings.AddTabTabGoConverters().AddTabTabGoConverters();

        Assert.Single(options.Converters, c => c is JTokenSystemTextJsonConverterFactory);
        Assert.Single(settings.Converters, c => c is JsonNodeNewtonsoftConverter);
        Assert.Single(settings.Converters, c => c is JsonElementNewtonsoftConverter);
        Assert.Throws<ArgumentNullException>(() => JsonDefaults.AddTabTabGoConverters((JsonSerializerOptions)null!));
        Assert.Throws<ArgumentNullException>(() => JsonDefaults.AddTabTabGoConverters((Nj.JsonSerializerSettings)null!));
    }

    // --- JToken under System.Text.Json ----------------------------------------------------------------------------

    [Fact]
    public void JTokenFactory_WritesTokensInsteadOfEmptyArrays()
    {
        var holder = new Holder { Obj = JObject.Parse("{\"a\":1,\"b\":[true]}"), Tok = new JValue("s"), Arr = JArray.Parse("[1]"), Val = new JValue(5) };

        var json = JsonSerializer.Serialize(holder, SerializerEngine.JsonSerializationSettings);

        var back = JsonNode.Parse(json)!;
        Assert.Equal(1, back["obj"]!["a"]!.GetValue<int>());
        Assert.Equal("s", back["tok"]!.GetValue<string>());
        Assert.Equal(1, back["arr"]![0]!.GetValue<int>());
        Assert.Equal(5, back["val"]!.GetValue<int>());
    }

    [Fact]
    public void JTokenFactory_ReadsTokens_AndNullStaysNull()
    {
        var holder = JsonSerializer.Deserialize<Holder>("{\"obj\":{\"a\":1},\"tok\":[1,2],\"arr\":[3],\"val\":\"v\"}", SerializerEngine.JsonSerializationSettings)!;
        var empty = JsonSerializer.Deserialize<Holder>("{\"obj\":null,\"arr\":null}", SerializerEngine.JsonSerializationSettings)!;

        Assert.Equal(1, holder.Obj!["a"]!.Value<int>());
        Assert.IsType<JArray>(holder.Tok);
        Assert.Equal(3, holder.Arr![0].Value<int>());
        Assert.Equal("v", holder.Val!.Value<string>());
        Assert.Null(empty.Obj);
        Assert.Null(empty.Arr);
    }

    [Fact]
    public void JTokenFactory_WrongShape_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("{\"obj\":[1]}", SerializerEngine.JsonSerializationSettings));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("{\"arr\":{\"a\":1}}", SerializerEngine.JsonSerializationSettings));
    }

    [Fact]
    public void JTokenFactory_CanConvert()
    {
        var factory = new JTokenSystemTextJsonConverterFactory();

        Assert.True(factory.CanConvert(typeof(JObject)));
        Assert.True(factory.CanConvert(typeof(JToken)));
        Assert.False(factory.CanConvert(typeof(string)));
        Assert.False(factory.CanConvert(typeof(JsonNode)));
    }

    [Fact]
    public void JTokenInsideExtraProperties_IsSerialisedByTheSharedOptions()
    {
        var entity = new Entity();
        entity.ExtraProperties["j"] = JObject.Parse("{\"x\":1}");

        var json = JsonSerializer.Serialize(entity, SerializerEngine.JsonSerializationSettings);

        Assert.Equal(1, JsonNode.Parse(json)!["j"]!["x"]!.GetValue<int>()); // nested empty arrays before (N-20)
    }

    // --- JsonNode / JsonElement under Newtonsoft --------------------------------------------------------------------

    [Fact]
    public void JsonNodeConverter_RoundTripsTheNodeFamilyUnderNewtonsoft()
    {
        var settings = JsonDefaults.CreateNewtonsoftSettings();
        var holder = new NodeHolder
        {
            Obj = new JsonObject { ["a"] = 1 },
            Arr = new JsonArray(1, 2),
            Node = JsonValue.Create("n"),
            Val = JsonValue.Create(true),
        };

        var json = Nj.JsonConvert.SerializeObject(holder, settings);
        var back = Nj.JsonConvert.DeserializeObject<NodeHolder>(json, settings)!;

        Assert.Equal("{\"obj\":{\"a\":1},\"arr\":[1,2],\"node\":\"n\",\"val\":true}", json);
        Assert.Equal(1, back.Obj!["a"]!.GetValue<int>());
        Assert.Equal(2, back.Arr!.Count);
        Assert.Equal("n", back.Node!.GetValue<string>());
        Assert.True(back.Val!.GetValue<bool>());
    }

    [Fact]
    public void JsonNodeConverter_NullsAndWrongShape()
    {
        var settings = JsonDefaults.CreateNewtonsoftSettings();

        Assert.Null(Nj.JsonConvert.DeserializeObject<NodeHolder>("{\"obj\":null}", settings)!.Obj);
        Assert.Throws<Nj.JsonSerializationException>(() => Nj.JsonConvert.DeserializeObject<NodeHolder>("{\"obj\":[1]}", settings));
        var converter = new JsonNodeNewtonsoftConverter();
        Assert.True(converter.CanConvert(typeof(JsonObject)));
        Assert.True(converter.CanConvert(typeof(JsonNode)));
        Assert.False(converter.CanConvert(typeof(JObject)));
        var writer = new Newtonsoft.Json.Linq.JTokenWriter();
        converter.WriteJson(writer, null, new Nj.JsonSerializer());
        Assert.Equal(JTokenType.Null, writer.Token!.Type);
    }

    [Fact]
    public void JsonElementConverter_RoundTripsElementsAndDocuments()
    {
        var settings = JsonDefaults.CreateNewtonsoftSettings();
        var holder = new ElementHolder
        {
            El = JsonDocument.Parse("{\"a\":[1]}").RootElement.Clone(),
            MaybeEl = JsonDocument.Parse("5").RootElement.Clone(),
            Doc = JsonDocument.Parse("\"d\""),
        };

        var json = Nj.JsonConvert.SerializeObject(holder, settings);
        var back = Nj.JsonConvert.DeserializeObject<ElementHolder>(json, settings)!;

        Assert.Equal("{\"el\":{\"a\":[1]},\"maybeEl\":5,\"doc\":\"d\"}", json);
        Assert.Equal(1, back.El.GetProperty("a")[0].GetInt32());
        Assert.Equal(5, back.MaybeEl!.Value.GetInt32());
        Assert.Equal("d", back.Doc!.RootElement.GetString());
    }

    [Fact]
    public void JsonElementConverter_NullsUndefinedAndCanConvert()
    {
        var settings = JsonDefaults.CreateNewtonsoftSettings();
        var converter = new JsonElementNewtonsoftConverter();

        var back = Nj.JsonConvert.DeserializeObject<ElementHolder>("{\"doc\":null,\"maybeEl\":null}", settings)!;
        var json = Nj.JsonConvert.SerializeObject(new ElementHolder(), settings); // default(JsonElement) is undefined

        Assert.Null(back.Doc);
        Assert.Null(back.MaybeEl);
        Assert.Equal("{\"el\":null}", json);
        Assert.True(converter.CanConvert(typeof(JsonElement)));
        Assert.True(converter.CanConvert(typeof(JsonDocument)));
        Assert.False(converter.CanConvert(typeof(string)));
        var writer = new Newtonsoft.Json.Linq.JTokenWriter();
        converter.WriteJson(writer, "not an element", new Nj.JsonSerializer());
        Assert.Equal(JTokenType.Null, writer.Token!.Type);
    }
}
