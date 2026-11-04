using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nj = Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TabTabGo.Core.Json;

namespace TabTabGo.Core.Tests;

public class JsonInteropTests
{
    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // --- STJ -> Newtonsoft ----------------------------------------------------------------------------------------

    [Fact]
    public void ToJToken_Element_AllValueKinds()
    {
        var token = (JObject)Element("{\"s\":\"x\",\"i\":1,\"f\":1.5,\"t\":true,\"n\":null,\"a\":[1,2],\"o\":{\"k\":\"v\"}}").ToJToken();

        Assert.Equal(JTokenType.String, token["s"]!.Type);
        Assert.Equal(JTokenType.Integer, token["i"]!.Type);
        Assert.Equal(JTokenType.Float, token["f"]!.Type);
        Assert.Equal(JTokenType.Boolean, token["t"]!.Type);
        Assert.Equal(JTokenType.Null, token["n"]!.Type);
        Assert.Equal(2, ((JArray)token["a"]!).Count);
        Assert.Equal("v", token["o"]!["k"]!.Value<string>());
    }

    [Fact]
    public void ToJToken_IsoDatesStayStrings()
    {
        var token = Element("{\"d\":\"2020-01-02T03:04:05Z\"}").ToJToken();

        Assert.Equal(JTokenType.String, token["d"]!.Type); // Newtonsoft would otherwise turn it into a DateTime
        Assert.Equal("2020-01-02T03:04:05Z", token["d"]!.Value<string>());
    }

    [Fact]
    public void ToJToken_DecimalHandling_KeepsMoneyExact()
    {
        var element = Element("{\"price\":0.1000000000000000055511151231257827}");

        var asDouble = element.ToJToken();
        var asDecimal = element.ToJToken(new JsonInteropOptions { FloatParseHandling = Nj.FloatParseHandling.Decimal });

        Assert.IsType<double>(((JValue)asDouble["price"]!).Value);
        Assert.IsType<decimal>(((JValue)asDecimal["price"]!).Value);
        Assert.Equal(0.1000000000000000055511151231257827m, ((JValue)asDecimal["price"]!).Value);
    }

    [Fact]
    public void ToJToken_DepthLimit_IsEnforced()
    {
        var json = new string('[', 100) + new string(']', 100);
        var element = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 200 }).RootElement;

        Assert.ThrowsAny<Nj.JsonException>(() => element.ToJToken());
        Assert.NotNull(element.ToJToken(new JsonInteropOptions { MaxDepth = 200 }));
    }

    [Fact]
    public void ToJToken_UndefinedElement_IsJsonNull()
    {
        Assert.Equal(JTokenType.Null, default(JsonElement).ToJToken().Type);
    }

    [Fact]
    public void ToJToken_Node_NullIsJsonNullValue_ObjectsConvert()
    {
        Assert.Equal(JTokenType.Null, ((JsonNode?)null).ToJToken().Type);
        Assert.Equal(1, new JsonObject { ["a"] = 1 }.ToJToken()["a"]!.Value<int>());
    }

    [Fact]
    public void ToJToken_Document_UsesRoot()
    {
        using var document = JsonDocument.Parse("[1]");

        Assert.Single((JArray)document.ToJToken());
        Assert.Throws<ArgumentNullException>(() => ((JsonDocument)null!).ToJToken());
    }

    [Fact]
    public void ToJObjectAndToJArray_Convert_NullThrows()
    {
        Assert.Equal("v", new JsonObject { ["k"] = "v" }.ToJObject()["k"]!.Value<string>());
        Assert.Equal(2, new JsonArray(1, 2).ToJArray().Count);
        Assert.Throws<ArgumentNullException>(() => ((JsonObject)null!).ToJObject());
        Assert.Throws<ArgumentNullException>(() => ((JsonArray)null!).ToJArray());
    }

    // --- Newtonsoft -> STJ ----------------------------------------------------------------------------------------

    [Fact]
    public void ToJsonElement_IsDetached_AndDatesAreIso()
    {
        var obj = new JObject { ["d"] = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), ["n"] = 5 };

        var element = obj.ToJsonElement();
        obj["n"] = 99; // the element must not follow

        Assert.Equal("2020-01-02T03:04:05.0000000Z", element.GetProperty("d").GetString());
        Assert.Equal(5, element.GetProperty("n").GetInt32());
        Assert.Throws<ArgumentNullException>(() => ((JToken)null!).ToJsonElement());
    }

    [Fact]
    public void ToJsonNode_NullNullTokenAndUndefined_AreNull()
    {
        Assert.Null(((JToken?)null).ToJsonNode());
        Assert.Null(JValue.CreateNull().ToJsonNode());
        Assert.Null(JValue.CreateUndefined().ToJsonNode());
        Assert.Equal(3, JToken.FromObject(3).ToJsonNode()!.GetValue<int>());
    }

    [Fact]
    public void ToJsonObjectArrayDocument_Convert()
    {
        Assert.Equal("v", JObject.Parse("{\"k\":\"v\"}").ToJsonObject()["k"]!.GetValue<string>());
        Assert.Equal(2, JArray.Parse("[1,2]").ToJsonArray().Count);
        using var document = JArray.Parse("[1]").ToJsonDocument();
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Throws<ArgumentNullException>(() => ((JObject)null!).ToJsonObject());
        Assert.Throws<ArgumentNullException>(() => ((JArray)null!).ToJsonArray());
        Assert.Throws<ArgumentNullException>(() => ((JToken)null!).ToJsonDocument());
    }

    [Fact]
    public void ToJsonElement_EveryNewtonsoftValueType()
    {
        var obj = new JObject
        {
            ["null"] = JValue.CreateNull(),
            ["undef"] = JValue.CreateUndefined(),
            ["bool"] = true,
            ["int"] = 5,
            ["long"] = long.MaxValue,
            ["ulong"] = ulong.MaxValue,
            ["short"] = (short)3,
            ["byte"] = (byte)4,
            ["sbyte"] = (sbyte)-4,
            ["ushort"] = (ushort)6,
            ["uint"] = 7u,
            ["big"] = new JValue(BigInteger.Parse("123456789012345678901234567890")),
            ["dec"] = 1.25m,
            ["dbl"] = 2.5,
            ["flt"] = 0.5f,
            ["nan"] = double.NaN,
            ["inf"] = float.PositiveInfinity,
            ["str"] = "s",
            ["bytes"] = new byte[] { 1, 2, 3 },
            ["guid"] = new Guid("11111111-1111-1111-1111-111111111111"),
            ["ts"] = TimeSpan.FromMinutes(90),
            ["uri"] = new Uri("http://x/y"),
            ["dto"] = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            ["raw"] = new JRaw("{\"r\":1}"),
            ["prop"] = new JProperty("p", 1).Value,
        };

        var element = obj.ToJsonElement();

        Assert.Equal(JsonValueKind.Null, element.GetProperty("null").ValueKind);
        Assert.Equal(JsonValueKind.Null, element.GetProperty("undef").ValueKind);
        Assert.True(element.GetProperty("bool").GetBoolean());
        Assert.Equal(long.MaxValue, element.GetProperty("long").GetInt64());
        Assert.Equal(ulong.MaxValue, element.GetProperty("ulong").GetUInt64());
        Assert.Equal(7u, element.GetProperty("uint").GetUInt32());
        Assert.Equal("123456789012345678901234567890", element.GetProperty("big").GetRawText());
        Assert.Equal(1.25m, element.GetProperty("dec").GetDecimal());
        Assert.Equal(2.5, element.GetProperty("dbl").GetDouble());
        Assert.Equal(JsonValueKind.Null, element.GetProperty("nan").ValueKind); // no JSON representation
        Assert.Equal(JsonValueKind.Null, element.GetProperty("inf").ValueKind);
        Assert.Equal("AQID", element.GetProperty("bytes").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", element.GetProperty("guid").GetString());
        Assert.Equal("01:30:00", element.GetProperty("ts").GetString());
        Assert.Equal("http://x/y", element.GetProperty("uri").GetString());
        Assert.StartsWith("2020-01-01T00:00:00.0000000+02:00", element.GetProperty("dto").GetString());
        Assert.Equal(1, element.GetProperty("raw").GetProperty("r").GetInt32());
        Assert.Equal(1, element.GetProperty("prop").GetInt32());
    }

    // --- object bridges -------------------------------------------------------------------------------------------

    private sealed record Sample(string Name, int Count);

    [Fact]
    public void DeserializeWithSystemTextJson_UsesCamelCaseOptionsByDefault()
    {
        var result = JObject.Parse("{\"name\":\"a\",\"count\":2}").DeserializeWithSystemTextJson<Sample>();

        Assert.Equal(new Sample("a", 2), result);
        Assert.Equal("b", JObject.Parse("{\"Name\":\"b\",\"Count\":1}").DeserializeWithSystemTextJson<Sample>(new JsonSerializerOptions())!.Name);
        Assert.Throws<ArgumentNullException>(() => ((JToken)null!).DeserializeWithSystemTextJson<Sample>());
    }

    [Fact]
    public void DeserializeWithNewtonsoft_Works_WithDefaultsAndCustomSettings()
    {
        var element = Element("{\"name\":\"a\",\"count\":2}");

        Assert.Equal(new Sample("a", 2), element.DeserializeWithNewtonsoft<Sample>());
        Assert.Equal("a", element.DeserializeWithNewtonsoft<Sample>(new Nj.JsonSerializerSettings())!.Name);
    }

    [Fact]
    public void SerializeToJTokenWithSystemTextJson_NullAndValue()
    {
        Assert.Equal(JTokenType.Null, JsonInterop.SerializeToJTokenWithSystemTextJson(null).Type);
        var token = JsonInterop.SerializeToJTokenWithSystemTextJson(new Sample("n", 3));
        Assert.Equal("n", token["name"]!.Value<string>());
        Assert.Equal("N", JsonInterop.SerializeToJTokenWithSystemTextJson(new Sample("N", 1), new JsonSerializerOptions())["Name"]!.Value<string>());
    }

    [Fact]
    public void SerializeToJsonNodeWithNewtonsoft_NullAndValue()
    {
        Assert.Null(JsonInterop.SerializeToJsonNodeWithNewtonsoft(null));
        Assert.Equal("n", JsonInterop.SerializeToJsonNodeWithNewtonsoft(new Sample("n", 3))!["name"]!.GetValue<string>());
        Assert.Equal("n", JsonInterop.SerializeToJsonNodeWithNewtonsoft(new Sample("n", 3), new Nj.JsonSerializerSettings())!["Name"]!.GetValue<string>());
    }

    // --- CLR values -----------------------------------------------------------------------------------------------

    [Fact]
    public void ToClrValue_Element_EveryKind()
    {
        var value = (Dictionary<string, object?>)Element("{\"b\":true,\"f\":false,\"s\":\"x\",\"i\":5,\"d\":1.5,\"huge\":1e30,\"n\":null,\"a\":[1,\"x\"],\"o\":{\"k\":1},\"dup\":1,\"dup\":2}").ToClrValue()!;

        Assert.Equal(true, value["b"]);
        Assert.Equal(false, value["f"]);
        Assert.Equal("x", value["s"]);
        Assert.Equal(5L, value["i"]);
        Assert.Equal(1.5m, value["d"]);
        Assert.Equal(1e30, value["huge"]); // does not fit a decimal, so double
        Assert.Null(value["n"]);
        Assert.Equal(new object?[] { 1L, "x" }, (List<object?>)value["a"]!);
        Assert.Equal(1L, ((Dictionary<string, object?>)value["o"]!)["k"]);
        Assert.Equal(2L, value["dup"]); // last wins
        Assert.Null(default(JsonElement).ToClrValue());
    }

    [Fact]
    public void ToClrValue_Node()
    {
        Assert.Null(((JsonNode?)null).ToClrValue());
        Assert.Equal(7L, JsonValue.Create(7).ToClrValue());
        Assert.Equal("s", JsonValue.Create("s").ToClrValue());
        Assert.IsType<Dictionary<string, object?>>(new JsonObject { ["a"] = 1 }.ToClrValue());
    }

    [Fact]
    public void ToClrValue_Token_EveryKind()
    {
        Assert.Null(((JToken?)null).ToClrValue());
        Assert.Null(JValue.CreateNull().ToClrValue());
        Assert.Null(JValue.CreateUndefined().ToClrValue());
        Assert.Equal(5L, new JValue(5).ToClrValue());
        Assert.Equal(5L, new JValue(5L).ToClrValue());
        Assert.Equal(5L, new JValue(new BigInteger(5)).ToClrValue());
        Assert.Equal(7L, new JValue(7UL).ToClrValue());
        Assert.Equal(1.5m, new JValue(1.5m).ToClrValue());
        Assert.Equal(0.1m, new JValue(0.1).ToClrValue()); // shortest round-trip text, not 0.1000000000000000055
        Assert.Equal(true, new JValue(true).ToClrValue());
        Assert.Equal("s", new JValue("s").ToClrValue());
        Assert.Equal("2020-01-01T00:00:00.0000000Z", new JValue(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToClrValue());
        Assert.StartsWith("2020-01-01T00:00:00.0000000+01:00", (string)new JValue(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(1))).ToClrValue()!);
        Assert.Equal("11111111-1111-1111-1111-111111111111", new JValue(new Guid("11111111-1111-1111-1111-111111111111")).ToClrValue());
        Assert.Equal(decimal.Parse("1234567890123456789012345678"), new JValue(BigInteger.Parse("1234567890123456789012345678")).ToClrValue());
        Assert.Equal(1e40, new JValue(BigInteger.Pow(10, 40)).ToClrValue());
        Assert.Equal((decimal)ulong.MaxValue, new JValue(ulong.MaxValue).ToClrValue());
        Assert.Equal(double.NaN, new JValue(double.NaN).ToClrValue());
        var dictionary = (Dictionary<string, object?>)JObject.Parse("{\"a\":[1,{\"b\":2}]}").ToClrValue()!;
        Assert.Equal(1L, ((List<object?>)dictionary["a"]!)[0]);
        Assert.Equal("p", new JProperty("name", "p").ToClrValue()?.ToString()?.Replace("\"name\": ", string.Empty).Trim('"'));
    }

    [Fact]
    public void NormalizeJsonValues_ReplacesEveryEngineSpecificValue_Recursively()
    {
        var bag = new Dictionary<string, object?>
        {
            ["element"] = Element("{\"x\":1}"),
            ["node"] = new JsonObject { ["y"] = 2 },
            ["token"] = JArray.Parse("[1,2]"),
            ["text"] = "plain",
            ["nothing"] = null,
            ["number"] = 5,
            ["nested"] = new Dictionary<string, object?> { ["deep"] = Element("3") },
            ["list"] = new object?[] { Element("4"), "z" },
        };

        var normalized = bag.NormalizeJsonValues();

        Assert.NotSame(bag, normalized);
        Assert.Equal(1L, ((Dictionary<string, object?>)normalized["element"]!)["x"]);
        Assert.Equal(2L, ((Dictionary<string, object?>)normalized["node"]!)["y"]);
        Assert.Equal(2, ((List<object?>)normalized["token"]!).Count);
        Assert.Equal("plain", normalized["text"]);
        Assert.Null(normalized["nothing"]);
        Assert.Equal(5, normalized["number"]);
        Assert.Equal(3L, ((IDictionary<string, object?>)normalized["nested"]!)["deep"]);
        Assert.Equal(new object?[] { 4L, "z" }, ((List<object?>)normalized["list"]!).ToArray());
        Assert.Throws<ArgumentNullException>(() => ((IDictionary<string, object?>)null!).NormalizeJsonValues());
    }

    // --- strings --------------------------------------------------------------------------------------------------

    [Fact]
    public void ParseJToken_ParseJsonNode_ToJsonString()
    {
        var token = JsonInterop.ParseJToken("{\"d\":\"2020-01-01T00:00:00Z\",\"n\":1.5}", new JsonInteropOptions { FloatParseHandling = Nj.FloatParseHandling.Decimal });

        Assert.Equal(JTokenType.String, token["d"]!.Type);
        Assert.IsType<decimal>(((JValue)token["n"]!).Value);
        Assert.Equal(1, JsonInterop.ParseJsonNode("{\"a\":1}")!["a"]!.GetValue<int>());
        Assert.Equal("{\"a\":1}", JObject.Parse("{\"a\":1}").ToJsonString());
        Assert.Contains("\n", JObject.Parse("{\"a\":1}").ToJsonString(indented: true));
        Assert.Equal("{\"t\":\"<a>\"}", JObject.Parse("{\"t\":\"<a>\"}").ToJsonString()); // relaxed escaping keeps text readable
        Assert.Throws<ArgumentNullException>(() => JsonInterop.ParseJToken(null!));
        Assert.Throws<ArgumentNullException>(() => JsonInterop.ParseJsonNode(null!));
        Assert.Throws<ArgumentNullException>(() => ((JToken)null!).ToJsonString());
    }

    [Fact]
    public void JsonEquals_IgnoresOrderAndNumberFormatting_ComparesDatesAsStrings()
    {
        var left = JObject.Parse("{\"a\":1,\"b\":[1,2],\"c\":{\"x\":1.0}}");

        Assert.True(left.JsonEquals(Element("{\"c\":{\"x\":1},\"b\":[1,2],\"a\":1.0}")));
        Assert.True(left.JsonEquals(JsonNode.Parse("{\"b\":[1,2],\"a\":1,\"c\":{\"x\":1}}")));
        Assert.False(left.JsonEquals(Element("{\"a\":1,\"b\":[2,1],\"c\":{\"x\":1}}"))); // arrays stay ordered
        Assert.False(left.JsonEquals(JsonNode.Parse("{\"a\":2}")));
        Assert.True(new JObject { ["d"] = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) }.JsonEquals(Element("{\"d\":\"2020-01-01T00:00:00.0000000Z\"}")));
        Assert.True(JValue.CreateNull().JsonEquals((JsonNode?)null));
        Assert.Throws<ArgumentNullException>(() => ((JToken)null!).JsonEquals(Element("1")));
        Assert.Throws<ArgumentNullException>(() => ((JToken)null!).JsonEquals((JsonNode?)null));
    }

    [Fact]
    public void Options_Defaults()
    {
        Assert.Equal(Nj.FloatParseHandling.Double, JsonInteropOptions.Default.FloatParseHandling);
        Assert.Equal(64, JsonInteropOptions.Default.MaxDepth);
        Assert.Same(JsonInteropOptions.Default, JsonInteropOptions.Default);
    }
}
