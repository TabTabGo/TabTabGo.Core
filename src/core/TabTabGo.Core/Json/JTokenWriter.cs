using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using Nj = Newtonsoft.Json;
using NjLinq = Newtonsoft.Json.Linq;

namespace TabTabGo.Core.Json;

/// <summary>
/// Writes Newtonsoft trees to <see cref="Utf8JsonWriter"/>. Dates are written ISO-8601 round-trip ("O"); this is the one
/// code path every Newtonsoft to System.Text.Json conversion in this library goes through.
/// </summary>
internal static class JTokenWriter
{
    internal static void Write(Utf8JsonWriter writer, NjLinq.JToken? token)
    {
        switch (token)
        {
            case null:
                writer.WriteNullValue();
                break;
            case NjLinq.JObject obj:
                writer.WriteStartObject();
                foreach (var property in obj.Properties())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case NjLinq.JArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            case NjLinq.JProperty property:
                // a property on its own has no JSON representation; write its value
                Write(writer, property.Value);
                break;
            case NjLinq.JValue value:
                WriteValue(writer, value);
                break;
            default:
                writer.WriteStringValue(token.ToString());
                break;
        }
    }

    internal static byte[] ToUtf8Bytes(NjLinq.JToken? token)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions(false)))
        {
            Write(writer, token);
        }

        return buffer.WrittenSpan.ToArray();
    }

    internal static string ToJsonString(NjLinq.JToken? token, bool indented)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions(indented)))
        {
            Write(writer, token);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static JsonWriterOptions WriterOptions(bool indented) => new()
    {
        Indented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void WriteValue(Utf8JsonWriter writer, NjLinq.JValue value)
    {
        switch (value.Type)
        {
            case NjLinq.JTokenType.Null:
            case NjLinq.JTokenType.Undefined:
                writer.WriteNullValue();
                return;
            case NjLinq.JTokenType.Raw:
                writer.WriteRawValue(value.Value?.ToString() ?? "null");
                return;
        }

        switch (value.Value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case short sh:
                writer.WriteNumberValue(sh);
                break;
            case byte by:
                writer.WriteNumberValue(by);
                break;
            case sbyte sb:
                writer.WriteNumberValue(sb);
                break;
            case ushort us:
                writer.WriteNumberValue(us);
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case BigInteger big:
                writer.WriteRawValue(big.ToString(CultureInfo.InvariantCulture));
                break;
            case decimal d:
                writer.WriteNumberValue(d);
                break;
            case double dbl when double.IsFinite(dbl):
                writer.WriteNumberValue(dbl);
                break;
            case float f when float.IsFinite(f):
                writer.WriteNumberValue(f);
                break;
            case double or float:
                // NaN / Infinity have no JSON representation
                writer.WriteNullValue();
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                writer.WriteStringValue(Convert.ToBase64String(bytes));
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            case TimeSpan ts:
                writer.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Uri uri:
                writer.WriteStringValue(uri.OriginalString);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
                break;
        }
    }

    internal static NjLinq.JToken Read(string json, JsonInteropOptions options)
    {
        using var stringReader = new StringReader(json);
        using var reader = new Nj.JsonTextReader(stringReader)
        {
            DateParseHandling = Nj.DateParseHandling.None,
            FloatParseHandling = options.FloatParseHandling,
            MaxDepth = options.MaxDepth,
        };
        return NjLinq.JToken.Load(reader);
    }
}
