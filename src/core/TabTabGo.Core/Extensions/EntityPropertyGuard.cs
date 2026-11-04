using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net;
using TabTabGo.Core.Exceptions;

namespace TabTabGo.Core.Extensions;

/// <summary>
/// Decides which properties of an entity a client may change through JSON Patch or <see cref="JsonExtensions.Populate{T}"/>,
/// and bounds how much a client can put into <see cref="IEntity.ExtraProperties"/>.
/// Protected: the primary key (<c>Id</c>, <c>{Type}Id</c> or a <c>[Key]</c> property), the audit fields, <c>IsEnabled</c>
/// (soft delete goes through <c>Delete</c>) and navigation properties (related entities are changed through their own
/// endpoints, never merged in through their parent).
/// </summary>
public static class EntityPropertyGuard
{
    /// <summary>Most keys an entity may hold in <see cref="IEntity.ExtraProperties"/> when a client adds them.</summary>
    public const int MaxExtraProperties = 100;

    /// <summary>Longest key, in characters, a client may add to <see cref="IEntity.ExtraProperties"/>.</summary>
    public const int MaxExtraPropertyKeyLength = 100;

    /// <summary>Longest value, as JSON text, a client may add to <see cref="IEntity.ExtraProperties"/>.</summary>
    public const int MaxExtraPropertyValueLength = 16_384;

    private static readonly string[] AuditAndStateProperties =
    {
        nameof(IEntity.CreatedBy), nameof(IEntity.CreatedDate), nameof(IEntity.UpdatedBy), nameof(IEntity.UpdatedDate), nameof(IEntity.IsEnabled),
    };

    private static readonly ConcurrentDictionary<Type, HashSet<string>> Cache = new();

    /// <summary>Whether a client may not change the property (matched case-insensitively).</summary>
    /// <param name="entityType">The entity type</param>
    /// <param name="propertyName">Property name as sent by the client</param>
    public static bool IsProtected(Type entityType, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return !string.IsNullOrEmpty(propertyName) && Cache.GetOrAdd(entityType, Build).Contains(propertyName);
    }

    /// <summary>Names of the protected properties of the type.</summary>
    /// <param name="entityType">The entity type</param>
    public static IReadOnlyCollection<string> ProtectedProperties(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Cache.GetOrAdd(entityType, Build);
    }

    private static HashSet<string> Build(Type type)
    {
        var names = new HashSet<string>(AuditAndStateProperties, StringComparer.OrdinalIgnoreCase)
        {
            "Id",
            type.Name + "Id",
        };
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<KeyAttribute>() != null || IsNavigation(property.PropertyType))
            {
                names.Add(property.Name);
            }
        }

        return names;
    }

    private static bool IsNavigation(Type type)
    {
        if (typeof(IEntity).IsAssignableFrom(type))
        {
            return true;
        }

        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return false;
        }

        var element = type.IsArray ? type.GetElementType() : type.IsGenericType ? type.GetGenericArguments()[0] : null;
        return element != null && typeof(IEntity).IsAssignableFrom(element);
    }

    /// <summary>
    /// Throws when adding <paramref name="key"/> with <paramref name="value"/> to the bag would exceed the limits.
    /// </summary>
    /// <param name="bag">The extra properties of the entity</param>
    /// <param name="key">Key being set</param>
    /// <param name="value">Value being set</param>
    /// <exception cref="ApiException">A limit is exceeded (400)</exception>
    public static void EnsureExtraPropertyAllowed(IDictionary<string, object?> bag, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(bag);
        if (string.IsNullOrEmpty(key) || key.Length > MaxExtraPropertyKeyLength)
        {
            throw Limit($"An extra property name must be 1 to {MaxExtraPropertyKeyLength} characters.");
        }

        if (!bag.ContainsKey(key) && bag.Count >= MaxExtraProperties)
        {
            throw Limit($"An entity can hold at most {MaxExtraProperties} extra properties.");
        }

        if (value is not null && value is not string && value is not ValueType
            && JsonSerializer.Serialize(value, value.GetType(), SerializerEngine.JsonSerializationSettings).Length > MaxExtraPropertyValueLength
            || value is string text && text.Length > MaxExtraPropertyValueLength)
        {
            throw Limit($"An extra property value can be at most {MaxExtraPropertyValueLength} characters.");
        }
    }

    private static ApiException Limit(string message) =>
        new(message, statusCode: HttpStatusCode.BadRequest, code: "EXTRA_PROPERTIES_LIMIT", errorNumber: 5001);
}
