using System.Globalization;
using TabTabGo.Core.Json;

namespace TabTabGo.Core.Extensions;

/// <summary>
/// Helpers for JSON strings and nodes.
/// </summary>
public static class JsonExtensions
{
    /// <summary>Shared read-only serializer options.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = SerializerEngine.JsonSerializationSettings;

    /// <summary>Converts JSON text to XML text with root element "root".</summary>
    public static string? ConvertToXml(this string json)
    {
        return ConvertToXml(json, "root");
    }

    /// <summary>Converts JSON text to XML text.</summary>
    public static string? ConvertToXml(this string json, string rootFieldName)
    {
        var xml = SerializerEngine.JsonToXml(json, rootFieldName);
        return xml.ToString();
    }

    /// <summary>Converts a JSON node to XML text with root element "root".</summary>
    public static string ConvertToXml(this JsonNode jToken)
    {
        return ConvertToXml(jToken, "root");
    }

    /// <summary>Converts a JSON node to XML text; arrays are wrapped in an object named <paramref name="rootFieldName"/>.</summary>
    public static string ConvertToXml(this JsonNode jToken, string rootFieldName)
    {
        var sToken = jToken is JsonArray
            ? new JsonObject { { rootFieldName, jToken.DeepClone() } }.ToString()
            : jToken.ToString();
        return ConvertToXml(sToken) ?? string.Empty;
    }

    /// <summary>
    /// Converts a JSON object to a dictionary.
    /// </summary>
    /// <param name="jsonObject">Json object</param>
    /// <param name="usePathAsKey">If true make json path into dictionary Key</param>
    /// <param name="separator">Path separator, "_" by default</param>
    public static IDictionary<string, object?> ConvertToDictionary(this JsonObject? jsonObject, bool usePathAsKey = false, string? separator = null)
    {
        var result = new Dictionary<string, object?>();
        if (jsonObject == null) return result;
        if (usePathAsKey)
        {
            foreach (var (path, value) in SerializerEngine.ConvertJsonToDictionary(jsonObject, separator))
            {
                result[path] = value;
            }

            return result;
        }

        foreach (var (name, value) in jsonObject)
        {
            result[name] = value;
        }

        return result;
    }

    /// <summary>
    /// Copies the properties present in the JSON object onto the target. Properties missing from the JSON are left
    /// untouched; names match case-insensitively; nested objects are merged; unknown names go to
    /// <see cref="IEntity.ExtraProperties"/> when the target is an entity (bounded, see <see cref="EntityPropertyGuard"/>).
    /// On an entity the protected properties (key, audit fields, <c>IsEnabled</c>, related entities) are skipped.
    /// </summary>
    public static void Populate<T>(this JsonNode value, T target) where T : class => value.Populate(target, null);

    /// <summary>
    /// Same as <see cref="Populate{T}(JsonNode, T)"/> with your own rule for protected properties. The predicate receives the
    /// name of the CLR property the client name resolved to; it replaces the default rule (<see cref="EntityPropertyGuard"/>).
    /// </summary>
    /// <param name="value">The partial entity as JSON</param>
    /// <param name="target">The object to change</param>
    /// <param name="isProtected">True for properties that must not be set; null uses the default rule</param>
    public static void Populate<T>(this JsonNode value, T target, Func<string, bool>? isProtected) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(target);
        if (value is not JsonObject jsonObject)
        {
            throw new ArgumentException("Populate needs a JSON object.", nameof(value));
        }

        PopulateObject(jsonObject, target, target.GetType(), isProtected);
    }

    private static void PopulateObject(JsonObject source, object target, Type type, Func<string, bool>? isProtected)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToList();
        foreach (var (name, node) in source)
        {
            var property = EntityPropertyGuard.ResolveProperty(type, name);
            if (property != null && !properties.Contains(property))
            {
                property = null;
            }

            if (property == null)
            {
                if (target is IEntity entity)
                {
                    var value = node.ToClrValue();
                    EntityPropertyGuard.EnsureExtraPropertyAllowed(entity.ExtraProperties, name, value);
                    entity.ExtraProperties[name] = value;
                }

                continue;
            }

            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            {
                continue;
            }

            if (target is IEntity owner && property.Name == nameof(IEntity.ExtraProperties))
            {
                // merged key by key so that the limits apply; never assigned as a whole
                if (node is JsonObject bag)
                {
                    foreach (var (key, item) in bag)
                    {
                        var clr = item.ToClrValue();
                        EntityPropertyGuard.EnsureExtraPropertyAllowed(owner.ExtraProperties, key, clr);
                        owner.ExtraProperties[key] = clr;
                    }
                }

                continue;
            }

            // key, audit fields, IsEnabled and related entities are never set from a client payload
            if (target is IEntity && (isProtected?.Invoke(property.Name) ?? EntityPropertyGuard.IsProtected(type, property.Name)))
            {
                continue;
            }

            var propertyType = property.PropertyType;
            if (node is JsonObject childObject && IsMergeable(propertyType) && property.GetValue(target) is { } existing)
            {
                PopulateObject(childObject, existing, existing.GetType(), isProtected);
                continue;
            }

            if (!property.CanWrite || property.GetSetMethod() == null)
            {
                continue;
            }

            if (node is null)
            {
                if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) != null)
                {
                    property.SetValue(target, null);
                }

                continue;
            }

            property.SetValue(target, node.Deserialize(propertyType, SerializerEngine.JsonSerializationSettings));
        }
    }

    private static bool IsMergeable(Type type) =>
        type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type) && !typeof(JsonNode).IsAssignableFrom(type);

    /// <summary>Writes the object as indented JSON text.</summary>
    public static string Serialize(this JsonObject json)
    {
        return json.ToString();
    }

    /// <summary>Serializes any value with the shared options.</summary>
    public static string Serialize<T>(this T obj)
    {
        return JsonSerializer.Serialize(obj, JsonOptions);
    }
}
