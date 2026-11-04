namespace TabTabGo.Core;

/// <summary>
/// A tenant of the system.
/// </summary>
/// <typeparam name="TKey">Tenant key type</typeparam>
public interface ITenant<TKey> where TKey : struct
{
    /// <summary>Tenant name.</summary>
    string Name { get; set; }

    /// <summary>Tenant code.</summary>
    string Code { get; set; }

    /// <summary>Tenant id.</summary>
    TKey Id { get; set; }
}
