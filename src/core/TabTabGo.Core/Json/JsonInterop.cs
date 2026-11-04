using System.Globalization;
using System.Numerics;
using Nj = Newtonsoft.Json;
using NjLinq = Newtonsoft.Json.Linq;

namespace TabTabGo.Core.Json;

/// <summary>
/// Conversions between System.Text.Json (primary engine) and Newtonsoft.Json (kept for JSON Patch and legacy hosts).
/// All conversions go through JSON text; dates stay strings, numbers keep their value.
/// </summary>
public static class JsonInterop
{
    #region System.Text.Json -> Newtonsoft

    /// <summary>Converts a <see cref="JsonElement"/> to a Newtonsoft token.</summary>
    public static NjLinq.JToken ToJToken(this JsonElement element, JsonInteropOptions? options = null)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            return NjLinq.JValue.CreateNull();
        }

        return JTokenWriter.Read(element.GetRawText(), options ?? JsonInteropOptions.Default);
    }

    /// <summary>Converts a <see cref="JsonNode"/> to a Newtonsoft token; <c>null</c> becomes a JSON null value.</summary>
    public static NjLinq.JToken ToJToken(this JsonNode? node, JsonInteropOptions? options = null)
    {
        return node is null
            ? NjLinq.JValue.CreateNull()
            : JTokenWriter.Read(node.ToJsonString(), options ?? JsonInteropOptions.Default);
    }

    /// <summary>Converts the root of a <see cref="JsonDocument"/> to a Newtonsoft token.</summary>
    public static NjLinq.JToken ToJToken(this JsonDocument document, JsonInteropOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.RootElement.ToJToken(options);
    }

    /// <summary>Converts a <see cref="JsonObject"/> to a <see cref="NjLinq.JObject"/>.</summary>
    public static NjLinq.JObject ToJObject(this JsonObject obj, JsonInteropOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (NjLinq.JObject)obj.ToJToken(options);
    }

    /// <summary>Converts a <see cref="JsonArray"/> to a <see cref="NjLinq.JArray"/>.</summary>
    public static NjLinq.JArray ToJArray(this JsonArray array, JsonInteropOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(array);
        return (NjLinq.JArray)array.ToJToken(options);
    }

    #endregion

    #region Newtonsoft -> System.Text.Json

    /// <summary>Converts a Newtonsoft token to a detached <see cref="JsonElement"/> (safe to use after the source changes).</summary>
    public static JsonElement ToJsonElement(this NjLinq.JToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        using var document = JsonDocument.Parse(JTokenWriter.ToUtf8Bytes(token));
        return document.RootElement.Clone();
    }

    /// <summary>Converts a Newtonsoft token to a <see cref="JsonNode"/>; <c>null</c>, Null and Undefined tokens become <c>null</c>.</summary>
    public static JsonNode? ToJsonNode(this NjLinq.JToken? token)
    {
        if (token is null || token.Type is NjLinq.JTokenType.Null or NjLinq.JTokenType.Undefined)
        {
            return null;
        }

        return JsonNode.Parse(JTokenWriter.ToUtf8Bytes(token));
    }

    /// <summary>Converts a <see cref="NjLinq.JObject"/> to a <see cref="JsonObject"/>.</summary>
    public static JsonObject ToJsonObject(this NjLinq.JObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (JsonObject)obj.ToJsonNode()!;
    }

    /// <summary>Converts a <see cref="NjLinq.JArray"/> to a <see cref="JsonArray"/>.</summary>
    public static JsonArray ToJsonArray(this NjLinq.JArray array)
    {
        ArgumentNullException.ThrowIfNull(array);
        return (JsonArray)array.ToJsonNode()!;
    }

    /// <summary>Converts a Newtonsoft token to a <see cref="JsonDocument"/>. The caller disposes it.</summary>
    public static JsonDocument ToJsonDocument(this NjLinq.JToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return JsonDocument.Parse(JTokenWriter.ToUtf8Bytes(token));
    }

    #endregion

    #region Object bridges

    /// <summary>Deserializes a Newtonsoft token with System.Text.Json.</summary>
    public static T? DeserializeWithSystemTextJson<T>(this NjLinq.JToken token, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        return JsonSerializer.Deserialize<T>(JTokenWriter.ToUtf8Bytes(token), options ?? SerializerEngine.JsonSerializationSettings);
    }

    /// <summary>Deserializes a <see cref="JsonElement"/> with Newtonsoft.</summary>
    public static T? DeserializeWithNewtonsoft<T>(this JsonElement element, Nj.JsonSerializerSettings? settings = null)
    {
        return Nj.JsonConvert.DeserializeObject<T>(element.GetRawText(), settings ?? JsonDefaults.CreateNewtonsoftSettings());
    }

    /// <summary>Serializes a value with System.Text.Json and returns it as a Newtonsoft token.</summary>
    public static NjLinq.JToken SerializeToJTokenWithSystemTextJson(object? value, JsonSerializerOptions? options = null)
    {
        if (value is null)
        {
            return NjLinq.JValue.CreateNull();
        }

        var json = JsonSerializer.Serialize(value, value.GetType(), options ?? SerializerEngine.JsonSerializationSettings);
        return JTokenWriter.Read(json, JsonInteropOptions.Default);
    }

    /// <summary>Serializes a value with Newtonsoft and returns it as a <see cref="JsonNode"/>.</summary>
    public static JsonNode? SerializeToJsonNodeWithNewtonsoft(object? value, Nj.JsonSerializerSettings? settings = null)
    {
        if (value is null)
        {
            return null;
        }

        var json = Nj.JsonConvert.SerializeObject(value, settings ?? JsonDefaults.CreateNewtonsoftSettings());
        return JsonNode.Parse(json);
    }

    #endregion

    #region CLR values (property bags)

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to plain CLR values: null, bool, long, decimal (double when it does not fit),
    /// string, <c>Dictionary&lt;string, object?&gt;</c> (last duplicate wins) and <c>List&lt;object?&gt;</c>.
    /// </summary>
    public static object? ToClrValue(this JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                {
                    return l;
                }

                return element.TryGetDecimal(out var d) ? d : element.GetDouble();
            case JsonValueKind.Object:
                var dictionary = new Dictionary<string, object?>();
                foreach (var property in element.EnumerateObject())
                {
                    dictionary[property.Name] = property.Value.ToClrValue();
                }

                return dictionary;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(item.ToClrValue());
                }

                return list;
            default:
                return null;
        }
    }

    /// <summary>Converts a <see cref="JsonNode"/> to plain CLR values (see <see cref="ToClrValue(JsonElement)"/>).</summary>
    public static object? ToClrValue(this JsonNode? node)
    {
        return node is null ? null : JsonSerializer.SerializeToElement(node).ToClrValue();
    }

    /// <summary>Converts a Newtonsoft token to plain CLR values (see <see cref="ToClrValue(JsonElement)"/>).</summary>
    public static object? ToClrValue(this NjLinq.JToken? token)
    {
        switch (token)
        {
            case null:
                return null;
            case NjLinq.JObject obj:
                var dictionary = new Dictionary<string, object?>();
                foreach (var property in obj.Properties())
                {
                    dictionary[property.Name] = property.Value.ToClrValue();
                }

                return dictionary;
            case NjLinq.JArray array:
                return array.Select(item => item.ToClrValue()).ToList();
            case NjLinq.JValue value:
                return value.Type switch
                {
                    NjLinq.JTokenType.Null or NjLinq.JTokenType.Undefined => null,
                    NjLinq.JTokenType.Integer => value.Value switch
                    {
                        long l => l,
                        int i => (long)i,
                        BigInteger big when big >= long.MinValue && big <= long.MaxValue => (long)big,
                        BigInteger big => (object)(decimal.TryParse(big.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)
                            ? dec
                            : double.Parse(big.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture)),
                        ulong ul when ul <= long.MaxValue => (long)ul,
                        ulong ul => (decimal)ul,
                        var other => Convert.ToInt64(other, CultureInfo.InvariantCulture),
                    },
                    NjLinq.JTokenType.Float => FloatToClr(value.Value),
                    NjLinq.JTokenType.Boolean => value.Value,
                    NjLinq.JTokenType.Date => value.Value switch
                    {
                        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
                        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
                        var other => other?.ToString(),
                    },
                    _ => Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                };
            default:
                return token.ToString();
        }
    }

    private static object FloatToClr(object? value)
    {
        switch (value)
        {
            case decimal d:
                return d;
            case double dbl when double.IsFinite(dbl):
                return TryToDecimal(dbl, out var dec) ? dec : dbl;
            case float f when float.IsFinite(f):
                return TryToDecimal(f, out var dec2) ? dec2 : (double)f;
            default:
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
    }

    private static bool TryToDecimal(double value, out decimal result)
    {
        // "R" gives the shortest round-trippable text, so 0.1 becomes 0.1m and not 0.1000000000000000055...
        return decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Returns a new dictionary where every <see cref="JsonElement"/>, <see cref="JsonNode"/> and Newtonsoft token value
    /// (recursively, also inside nested dictionaries and lists) is replaced by its plain CLR value.
    /// </summary>
    public static IDictionary<string, object?> NormalizeJsonValues(this IDictionary<string, object?> bag)
    {
        ArgumentNullException.ThrowIfNull(bag);
        var result = new Dictionary<string, object?>(bag.Count);
        foreach (var pair in bag)
        {
            result[pair.Key] = NormalizeValue(pair.Value);
        }

        return result;
    }

    private static object? NormalizeValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonElement element:
                return element.ToClrValue();
            case JsonNode node:
                return node.ToClrValue();
            case NjLinq.JToken token:
                return token.ToClrValue();
            case string:
                return value;
            case IDictionary<string, object?> dictionary:
                return dictionary.NormalizeJsonValues();
            case IEnumerable enumerable:
                return enumerable.Cast<object?>().Select(NormalizeValue).ToList();
            default:
                return value;
        }
    }

    #endregion

    #region Strings

    /// <summary>Parses JSON text to a Newtonsoft token (dates stay strings).</summary>
    public static NjLinq.JToken ParseJToken(string json, JsonInteropOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JTokenWriter.Read(json, options ?? JsonInteropOptions.Default);
    }

    /// <summary>Parses JSON text to a <see cref="JsonNode"/>.</summary>
    public static JsonNode? ParseJsonNode(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonNode.Parse(json);
    }

    /// <summary>Writes a Newtonsoft token as JSON text (dates ISO-8601 round-trip).</summary>
    public static string ToJsonString(this NjLinq.JToken token, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(token);
        return JTokenWriter.ToJsonString(token, indented);
    }

    #endregion

    #region Semantic equality

    /// <summary>Compares a Newtonsoft token with a <see cref="JsonElement"/>: property order is ignored, numbers compare by value.</summary>
    public static bool JsonEquals(this NjLinq.JToken left, JsonElement right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return DeepEqualsTokens(Normalize(left), CanonicalNumbers(right.ToJToken(DecimalOptions)));
    }

    /// <summary>Compares a Newtonsoft token with a <see cref="JsonNode"/>: property order is ignored, numbers compare by value.</summary>
    public static bool JsonEquals(this NjLinq.JToken left, JsonNode? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return DeepEqualsTokens(Normalize(left), CanonicalNumbers(right.ToJToken(DecimalOptions)));
    }

    private static readonly JsonInteropOptions DecimalOptions = new() { FloatParseHandling = Nj.FloatParseHandling.Decimal };

    private static NjLinq.JToken Normalize(NjLinq.JToken token) =>
        CanonicalNumbers(JTokenWriter.Read(JTokenWriter.ToJsonString(token, false), DecimalOptions));

    private static IEnumerable<NjLinq.JValue> ValuesOf(NjLinq.JToken token)
    {
        if (token is NjLinq.JValue value)
        {
            yield return value;
            yield break;
        }

        foreach (var child in token.Children())
        {
            foreach (var inner in ValuesOf(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>Newtonsoft treats 1 (integer) and 1.0 (float) as different values; turn every number into a decimal so they compare by value.</summary>
    private static NjLinq.JToken CanonicalNumbers(NjLinq.JToken token)
    {
        foreach (var value in ValuesOf(token).ToList())
        {
            if (value.Type is NjLinq.JTokenType.Integer or NjLinq.JTokenType.Float && value.Value is not null)
            {
                try
                {
                    value.Value = Convert.ToDecimal(value.Value is BigInteger big ? (object)(decimal)big : value.Value, CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is OverflowException or InvalidCastException)
                {
                    // too large for a decimal: keep the original value
                }
            }
        }

        return token;
    }

    private static bool DeepEqualsTokens(NjLinq.JToken left, NjLinq.JToken right) => NjLinq.JToken.DeepEquals(left, right);

    #endregion
}
