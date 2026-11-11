namespace TabTabGo.Core.Extensions;

/// <summary>
/// Copy helpers for entities.
/// </summary>
public static class EntityExtensions
{
    /// <summary>
    /// Copies the public settable properties of <paramref name="type"/> from source to destination. Properties marked
    /// with <see cref="IgnoreCopyAttribute"/>, listed in <paramref name="ignoredProperties"/>, collections and
    /// properties without a public setter are skipped.
    /// </summary>
    public static void CopyObject(this object source, Type type, object destination,
        params string[] ignoredProperties)
    {
        CopyProperties(source, type, destination, ignoredProperties, honourIgnoreCopy: true);
    }

    /// <summary>
    /// Copies the properties of source into destination (see <see cref="CopyObject(object, Type, object, string[])"/>).
    /// A new destination is created when none is given.
    /// </summary>
    public static void CopyObject<T>(this T source, T? destination, params string[] ignoredProperties)
        where T : class
    {
        destination ??= Clone(source);
        source.CopyObject(typeof(T), destination, ignoredProperties);
    }

    /// <summary>
    /// Creates a new instance of <typeparamref name="T"/> with the public properties of source (shallow copy,
    /// <see cref="IgnoreCopyAttribute"/> is not applied, collections are not copied).
    /// </summary>
    public static T Clone<T>(this T source) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var newObj = Activator.CreateInstance<T>();
        CopyProperties(source, typeof(T), newObj, Array.Empty<string>(), honourIgnoreCopy: false);
        return newObj;
    }

    private static void CopyProperties(object source, Type type, object destination, string[]? ignoredProperties,
        bool honourIgnoreCopy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        foreach (var prop in type.GetProperties())
        {
            if (honourIgnoreCopy && prop.GetCustomAttributes<IgnoreCopyAttribute>().Any(a => a.IgnoreCopy)) continue;
            if (ignoredProperties != null && ignoredProperties.Contains(prop.Name)) continue;
            if (!prop.CanRead || prop.GetSetMethod() == null || prop.GetIndexParameters().Length > 0) continue;

            if (prop.PropertyType.IsArray
                || (prop.PropertyType.IsGenericType &&
                    prop.PropertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                || (prop.PropertyType.IsGenericType &&
                    prop.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>)))
            {
                continue;
            }

            if (prop.PropertyType != typeof(string) && prop.PropertyType.IsClass &&
                prop.PropertyType.GetInterface("IEntity", true) == typeof(IEntity))
            {
                if (honourIgnoreCopy && prop.PropertyType.GetCustomAttributes<IgnoreCopyAttribute>().Any(a => a.IgnoreCopy)) continue;

                var sourceChild = prop.GetValue(source);
                if (sourceChild == null) continue;

                var destinationChild = prop.GetValue(destination);
                if (destinationChild != null)
                {
                    CopyProperties(sourceChild, prop.PropertyType, destinationChild, ignoredProperties, honourIgnoreCopy);
                    continue;
                }
            }

            prop.SetValue(destination, prop.GetValue(source));
        }
    }
}

/// <summary>
/// Excludes a property (or all properties of a type) from <see cref="EntityExtensions.CopyObject(object, Type, object, string[])"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class IgnoreCopyAttribute : Attribute
{
    /// <summary>When true the property is not copied.</summary>
    public bool IgnoreCopy { get; set; }

    /// <summary>Creates the attribute.</summary>
    /// <param name="ignore">Whether to ignore the property</param>
    public IgnoreCopyAttribute(bool ignore = true)
    {
        IgnoreCopy = ignore;
    }
}
