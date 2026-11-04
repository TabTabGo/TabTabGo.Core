namespace TabTabGo.Core.Attributes;

/// <summary>
/// Names an enum member for <see cref="Converters.JsonEnumConverter{T}"/>.
/// </summary>
/// <param name="name">Name used in JSON</param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class JsonEnumNameAttribute(string name) : Attribute
{
    /// <summary>Name used in JSON.</summary>
    public string Name { get; } = name;
}
