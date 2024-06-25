namespace TabTabGo.Core.Attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class JsonEnumNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}