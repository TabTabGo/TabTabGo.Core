namespace TabTabGo.Core.Data;

/// <summary>
/// A pending change of the unit of work: the entity, its state and a snapshot of the values.
/// </summary>
/// <param name="Entity">The tracked entity</param>
/// <param name="State">Entity state name (Added, Modified, Deleted)</param>
/// <param name="OriginalValues">Property values when the entity was loaded (empty for added entities)</param>
/// <param name="CurrentValues">Property values now (snapshot, not live)</param>
public sealed record EntityChange(
    object Entity,
    string State,
    IReadOnlyDictionary<string, object?> OriginalValues,
    IReadOnlyDictionary<string, object?> CurrentValues);
