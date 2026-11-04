namespace TabTabGo.Core.Comparer;

/// <summary>
/// Compares two entities property by property (audit fields and collections are ignored) and records
/// the properties that differ.
/// </summary>
public class EntityComparer : IEqualityComparer<IEntity>, IDisposable
{
    private static readonly HashSet<string> AuditProperties = new()
    {
        nameof(IEntity.CreatedBy), nameof(IEntity.CreatedDate), nameof(IEntity.UpdatedBy), nameof(IEntity.UpdatedDate)
    };

    /// <summary>Properties that differed in the last comparison.</summary>
    public List<PropertyInfo> DifferentProperties { get; set; } = new();

    /// <summary>Names of the properties that differed in the last comparison.</summary>
    public IEnumerable<string> GetDifferentProeprties()
    {
        return DifferentProperties.Select(prop => prop.Name).ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DifferentProperties.Clear();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public bool Equals(IEntity? x, IEntity? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        var isEqual = true;
        var properties = x.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            if (AuditProperties.Contains(prop.Name) || prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;

            var xValue = prop.GetValue(x);
            var yValue = prop.GetValue(y);
            if (xValue == null && yValue == null) continue;
            if (xValue == null || yValue == null)
            {
                DifferentProperties.Add(prop);
                isEqual = false;
                continue;
            }

            if (xValue is IEntity xEntity && yValue is IEntity yEntity)
            {
                using var entityComparer = new EntityComparer();
                if (!entityComparer.Equals(xEntity, yEntity))
                {
                    DifferentProperties.Add(prop);
                    isEqual = false;
                }
            }
            else if (xValue is string || !typeof(IEnumerable).IsAssignableFrom(prop.PropertyType))
            {
                // collections (including ExtraProperties) are not compared
                if (!xValue.Equals(yValue))
                {
                    DifferentProperties.Add(prop);
                    isEqual = false;
                }
            }
        }

        return isEqual;
    }

    /// <inheritdoc />
    public int GetHashCode(IEntity obj)
    {
        return obj.GetHashCode();
    }
}
