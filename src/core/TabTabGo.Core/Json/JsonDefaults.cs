using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Json;

/// <summary>
/// Ready-made serializer configuration for both engines, and idempotent helpers that register the TabTabGo converters
/// on options a host already owns.
/// </summary>
public static class JsonDefaults
{
    /// <summary>A mutable copy of the library's System.Text.Json settings (camelCase, enums as camelCase strings, nulls skipped).</summary>
    public static JsonSerializerOptions CreateSystemTextJsonOptions() => SerializerEngine.CreateJsonSerializerOptions();

    /// <summary>
    /// New Newtonsoft settings that mirror the System.Text.Json defaults: camelCase names, camelCase string enums,
    /// nulls ignored, dates never parsed, and converters for <see cref="JsonNode"/> and <see cref="JsonElement"/>.
    /// </summary>
    public static Nj.JsonSerializerSettings CreateNewtonsoftSettings()
    {
        var settings = new Nj.JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = Nj.NullValueHandling.Ignore,
            DateParseHandling = Nj.DateParseHandling.None,
        };
        settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
        return settings.AddTabTabGoConverters();
    }

    /// <summary>Registers the converter that lets System.Text.Json handle Newtonsoft tokens. Safe to call twice.</summary>
    public static JsonSerializerOptions AddTabTabGoConverters(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Converters.Any(c => c is JTokenSystemTextJsonConverterFactory))
        {
            options.Converters.Add(new JTokenSystemTextJsonConverterFactory());
        }

        return options;
    }

    /// <summary>Registers the converters that let Newtonsoft handle <see cref="JsonNode"/> and <see cref="JsonElement"/>. Safe to call twice.</summary>
    public static Nj.JsonSerializerSettings AddTabTabGoConverters(this Nj.JsonSerializerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Converters.Any(c => c is JsonNodeNewtonsoftConverter))
        {
            settings.Converters.Add(new JsonNodeNewtonsoftConverter());
        }

        if (!settings.Converters.Any(c => c is JsonElementNewtonsoftConverter))
        {
            settings.Converters.Add(new JsonElementNewtonsoftConverter());
        }

        return settings;
    }
}
