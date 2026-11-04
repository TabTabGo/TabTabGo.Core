namespace TabTabGo.Core.Enums;

/// <summary>
/// Flags that change how repositories build a query.
/// </summary>
[Flags]
public enum QueryFlags
{
    /// <summary>Do not track entities.</summary>
    DisableTracking = 1,

    /// <summary>Skip the fix/scope criteria.</summary>
    IgnoreFixQuery = 2,

    /// <summary>Expand predicates (LinqKit) before running the query.</summary>
    IsExpandable = 4,

    /// <summary>Use the context-local (tracked) entities.</summary>
    UseLocal = 8
}
