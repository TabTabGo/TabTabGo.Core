using System.Linq.Expressions;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;

namespace TabTabGo.Core.Services;

/// <summary>
/// Read and write service for an entity.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TKey">Entity key type</typeparam>
public interface IBaseService<TEntity, TKey> : IBaseReadService<TEntity, TKey> where TEntity : class, IEntity
{
    #region Update Functions

    /// <summary>Replaces the stored entity with the given one.</summary>
    /// <param name="id">Entity key</param>
    /// <param name="entity">New values</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<TEntity> Update(TKey id, TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Copies the changed values of the given entity onto the stored one.</summary>
    /// <param name="id">Entity key</param>
    /// <param name="entity">Entity with the changes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<TEntity> UpdateChanges(TKey id, TEntity entity, CancellationToken cancellationToken);

    /// <summary>Applies a JSON patch document to the stored entity.</summary>
    /// <param name="id">Entity key</param>
    /// <param name="entity">The patch document</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<TEntity> Update(TKey id, JsonPatchDocument<TEntity> entity, CancellationToken cancellationToken = default);

    /// <summary>Merges the properties present in a JSON object onto the stored entity.</summary>
    /// <param name="id">Entity key</param>
    /// <param name="entity">Partial entity as JSON</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<TEntity> Update(TKey id, JsonObject entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the changes made to an entity this service loaded, and only keeps them when the entity is still inside
    /// <paramref name="scope"/> afterwards (checked in the database, so foreign keys and navigations count). When it left the
    /// scope the changes are rolled back and a 404 <c>ApiException</c> is thrown. The default implementation ignores the scope.
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <param name="entity">The changed entity</param>
    /// <param name="scope">Scope the entity must stay in; null means no scope</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<TEntity> UpdateChanges(TKey id, TEntity entity, Expression<Func<TEntity, bool>>? scope, CancellationToken cancellationToken)
        => UpdateChanges(id, entity, cancellationToken);

    /// <summary>
    /// Merges a partial JSON object into an entity the way <see cref="Update(TKey, JsonObject, CancellationToken)"/> does
    /// (protected properties are skipped, extra properties are bounded). The default implementation uses the default rules.
    /// </summary>
    /// <param name="changes">The partial entity</param>
    /// <param name="entity">The entity to change</param>
    void ApplyJson(JsonObject changes, TEntity entity) => Extensions.JsonExtensions.Populate(changes, entity);

    /// <summary>Applies one JSON patch operation to an object.</summary>
    /// <param name="operation">The operation</param>
    /// <param name="affectedObject">The object to change</param>
    void HandleJsonOperator(Operation operation, object affectedObject);

    /// <summary>
    /// Rejects a JSON Patch document that touches a protected property (key, audit fields, <c>IsEnabled</c>, related
    /// entities). The default implementation accepts everything; <c>BaseService</c> enforces the rule.
    /// </summary>
    /// <param name="changes">The patch document</param>
    /// <exception cref="Exceptions.ApiException">A protected property is patched (400)</exception>
    void EnsurePatchable(JsonPatchDocument<TEntity> changes)
    {
    }

    /// <summary>
    /// Checks the <see cref="IEntity.ExtraProperties"/> of an entity after a client patch was applied to it: key count, key
    /// length and value size. Call it before saving. The default implementation accepts everything.
    /// </summary>
    /// <param name="entity">The patched entity</param>
    /// <exception cref="Exceptions.ApiException">A limit is exceeded (400)</exception>
    void EnsureExtraPropertiesWithinLimits(TEntity entity)
    {
    }

    /// <summary>
    /// Rejects a partial JSON object that names a protected property. The default implementation accepts everything.
    /// </summary>
    /// <param name="changes">The partial entity</param>
    /// <exception cref="Exceptions.ApiException">A protected property is named (400)</exception>
    void EnsurePatchable(JsonObject changes)
    {
    }

    #endregion

    /// <summary>Creates an entity.</summary>
    /// <param name="entity">The entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<TEntity> Create(TEntity? entity, CancellationToken cancellationToken = default);

    /// <summary>Deletes an entity.</summary>
    /// <param name="id">Entity key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<TEntity> Delete(TKey id, CancellationToken cancellationToken = default);

    /// <summary>Whether the entity can be deleted.</summary>
    /// <param name="id">Entity key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<bool> CanDelete(TKey id, CancellationToken cancellationToken = default);

    /// <summary>Whether all the entities can be deleted.</summary>
    /// <param name="ids">Entity keys</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<bool> CanDelete(IEnumerable<TKey> ids, CancellationToken cancellationToken);

    /// <summary>Deletes several entities.</summary>
    /// <param name="ids">Entity keys</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<IEnumerable<TEntity>> Delete(IEnumerable<TKey> ids, CancellationToken cancellationToken);
}
