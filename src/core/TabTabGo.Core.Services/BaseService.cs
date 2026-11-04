using System.Linq.Expressions;
using TabTabGo.Core.Enums;
using System.Text.Json.Nodes;
﻿using System.Net;
using System.Reflection;
using LinqKit;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Data;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Extensions;
using TabTabGo.Core.Json;

namespace TabTabGo.Core.Services;

/// <summary>
/// Read and write service for an entity: create, update (full, changes, JSON patch, partial JSON) and soft delete with
/// validation hooks. Failures are <see cref="ApiException"/>s with a status code: 404 when the entity does not exist,
/// 400 when a validation hook rejects the operation.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TKey">Entity key type</typeparam>
public abstract class BaseService<TEntity, TKey> : BaseReadService<TEntity, TKey>, IBaseService<TEntity, TKey>
    where TEntity : class, IEntity
{
    /// <summary>The unit of work that saves the changes.</summary>
    protected readonly IUnitOfWork _unitOfWork;

    /// <summary>The write repository.</summary>
    public new IGenericRepository<TEntity, TKey> CurrentRepository =>
        (IGenericRepository<TEntity, TKey>)base.CurrentRepository;

    /// <summary>Creates the service with an explicit repository.</summary>
    /// <param name="unitOfWork">Unit of work</param>
    /// <param name="repository">Repository</param>
    /// <param name="logger">Logger</param>
    public BaseService(IUnitOfWork unitOfWork, IGenericRepository<TEntity, TKey> repository,
        ILogger<BaseService<TEntity, TKey>> logger) : base(repository, logger)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <summary>Creates the service; the repository comes from the unit of work.</summary>
    /// <param name="unitOfWork">Unit of work</param>
    /// <param name="logger">Logger</param>
    public BaseService(IUnitOfWork unitOfWork,
        ILogger<BaseService<TEntity, TKey>> logger) : base(RepositoryOf(unitOfWork), logger)
    {
        _unitOfWork = unitOfWork;
    }

    private static IGenericRepository<TEntity, TKey> RepositoryOf(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        return unitOfWork.Repository<TEntity, TKey>();
    }

    private ApiException NotFound(TKey id) =>
        new($"{typeof(TEntity).Name} '{id}' was not found.", errorNumber: 1404, statusCode: HttpStatusCode.NotFound, code: "NotFound");

    private async Task<TEntity> LoadRequiredAsync(TKey id, CancellationToken cancellationToken, string[]? includeProperties = null)
    {
        await LoadEntityAsync(id, cancellationToken, includeProperties);
        return CurrentEntity ?? throw NotFound(id);
    }

    #region Create Methods

    /// <summary>
    /// Create Entity
    /// </summary>
    /// <param name="entity">The entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The stored entity, reloaded from the repository</returns>
    public virtual async Task<TEntity> Create(TEntity? entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!IsValidToCreate(entity, out var errorMessage))
            throw new ApiException($"Failed to create {typeof(TEntity).Name}. {errorMessage}", errorNumber: 2001, statusCode: HttpStatusCode.BadRequest);
        await PreCreate(entity, cancellationToken);
        var newEntity = await CurrentRepository.InsertAsync(entity, cancellationToken) ?? entity;
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await LoadEntityAsync(GetKey(newEntity), cancellationToken);
        var created = CurrentEntity ?? newEntity;
        await PostCreate(created, cancellationToken);
        return created;
    }

    /// <summary>
    /// Validate passed entity before createed in DB
    /// </summary>
    /// <param name="entity">The entity</param>
    /// <param name="validationErrorMessage">Reason when invalid</param>
    public virtual bool IsValidToCreate(TEntity entity, out string validationErrorMessage)
    {
        validationErrorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Post Create methods
    /// </summary>
    /// <param name="entity">The stored entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task PostCreate(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>Runs before the entity is inserted.</summary>
    /// <param name="entity">The entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task PreCreate(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    #endregion

    #region Delete Methods

    /// <summary>
    /// Delete an entity ( Disable entity)
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ApiException">404 when missing, 400 when <see cref="IsValidToDelete"/> rejects it</exception>
    public virtual async Task<TEntity> Delete(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await LoadRequiredAsync(id, cancellationToken);

        if (!IsValidToDelete(entity, out var errorMessage))
            throw new ApiException($"Can't delete {typeof(TEntity).Name} '{id}'. {errorMessage}", errorNumber: 3001, statusCode: HttpStatusCode.BadRequest);
        await PreDelete(entity, cancellationToken);
        entity.IsEnabled = false;
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostDelete(entity, cancellationToken);
        return entity;
    }

    /// <summary>
    /// Validate if entity can be deleted
    /// </summary>
    /// <param name="entity">The entity</param>
    /// <param name="validationErrorMessage">Reason when it cannot</param>
    public virtual bool IsValidToDelete(TEntity entity, out string validationErrorMessage)
    {
        validationErrorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// function executed after the delete eneityt is completed
    /// </summary>
    /// <param name="entity">The entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task PostDelete(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>Runs before the entity is disabled.</summary>
    /// <param name="entity">The entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task PreDelete(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>Runs after several entities are disabled.</summary>
    /// <param name="entity">The entities</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task PostDelete(IEnumerable<TEntity> entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>Runs before several entities are disabled.</summary>
    /// <param name="entity">The entities</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task PreDelete(IEnumerable<TEntity> entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Function used to check if can delete entity from client side
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ApiException">404 when the entity does not exist</exception>
    public virtual async Task<bool> CanDelete(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await LoadRequiredAsync(id, cancellationToken);
        return IsValidToDelete(entity, out _);
    }

    /// <summary>
    /// Validates the delete of several entities. Entities that do not exist are not part of the result.
    /// </summary>
    /// <param name="ids">Entity keys</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>One entry per found entity: key, whether it can be deleted and the reason when not</returns>
    public virtual async Task<IReadOnlyList<(TKey Id, bool CanDelete, string Error)>> GetDeleteValidationAsync(
        IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        var entities = await LoadByKeysAsync(ids, cancellationToken);
        var results = new List<(TKey, bool, string)>();
        foreach (var entity in entities)
        {
            var canDelete = IsValidToDelete(entity, out var error);
            results.Add((GetKey(entity), canDelete, error));
        }

        return results;
    }

    /// <inheritdoc />
    public virtual async Task<bool> CanDelete(IEnumerable<TKey> ids, CancellationToken cancellationToken)
    {
        var validation = await GetDeleteValidationAsync(ids, cancellationToken);
        return validation.All(v => v.CanDelete);
    }

    /// <inheritdoc />
    public virtual async Task<IEnumerable<TEntity>> Delete(IEnumerable<TKey> ids, CancellationToken cancellationToken)
    {
        var enumerable = await LoadByKeysAsync(ids, cancellationToken);
        foreach (var entity in enumerable)
        {
            if (!IsValidToDelete(entity, out var errorMessage))
                throw new ApiException($"Can't delete {typeof(TEntity).Name} '{GetKey(entity)}'. {errorMessage}", errorNumber: 3002, statusCode: HttpStatusCode.BadRequest);
        }

        await PreDelete(enumerable, cancellationToken);
        foreach (var entity in enumerable)
        {
            entity.IsEnabled = false;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await PostDelete(enumerable, cancellationToken);
        return enumerable;
    }

    private async Task<TEntity[]> LoadByKeysAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
        {
            return Array.Empty<TEntity>();
        }

        // one IN list (or, without store metadata, a bounded OR): Contains(GetKey(e)) cannot be translated to SQL
        var predicate = KeysPredicate(keys);

        var entities = await CurrentRepository.GetAsync(filter: predicate, cancellationToken: cancellationToken);
        return entities as TEntity[] ?? entities.ToArray();
    }

    #endregion

    #region Update Methods

    /// <summary>
    ///  Update Entity: copies the values of <paramref name="entity"/> onto the stored entity and saves.
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <param name="entity">New values</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<TEntity> Update(TKey id, TEntity entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!EqualityComparer<TKey>.Default.Equals(GetKey(entity), id))
            throw new ApiException($"Failed to Update {typeof(TEntity).Name} '{id}'. The key in the body does not match the key in the route.", errorNumber: 4003, statusCode: HttpStatusCode.BadRequest);
        var includeProperties = GetIncludedProperties(entity, IgnoredProperties);
        var current = await LoadRequiredAsync(id, cancellationToken, includeProperties);
        if (!IsValidToUpdate(entity, out var errorMessage))
            throw new ApiException($"Failed to Update {typeof(TEntity).Name} '{id}'. {errorMessage}", errorNumber: 4001, statusCode: HttpStatusCode.BadRequest);
        await PreUpdate(current, entity, cancellationToken);
        entity.CopyObject(current, "IsEnabled");
        var updated = await CurrentRepository.UpdateAsync(current, cancellationToken) ?? current;
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostUpdate(updated, cancellationToken);
        return updated;
    }

    /// <summary>
    /// Update entity after process patch json on entity object
    /// </summary>
    /// <param name="id">entity identifier</param>
    /// <param name="entity">updated object</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <exception cref="ApiException"></exception>
    public virtual async Task<TEntity> UpdateChanges(TKey id, TEntity entity, CancellationToken cancellationToken)
    {
        var updated = await UpdateChangesCore(id, entity, null, cancellationToken);
        await PostUpdate(updated, cancellationToken);
        return updated;
    }

    /// <inheritdoc />
    public virtual async Task<TEntity> UpdateChanges(TKey id, TEntity entity, Expression<Func<TEntity, bool>>? scope,
        CancellationToken cancellationToken)
    {
        if (scope == null)
        {
            return await UpdateChanges(id, entity, cancellationToken);
        }

        // Save inside a transaction, ask the database whether the entity is still in scope (foreign keys and
        // navigations count there) and roll everything back when it is not. The unit of work runs the whole unit
        // through the provider's execution strategy, so retrying strategies (SQL Server, Npgsql) work too.
        // PostUpdate runs after the commit: a side effect must not fire for a change that can still be rolled back.
        var updated = await _unitOfWork.ExecuteInTransactionAsync(
            ct => UpdateChangesCore(id, entity, scope, ct), cancellationToken);
        await PostUpdate(updated, cancellationToken);
        return updated;
    }

    /// <summary>
    /// The update without <c>PostUpdate</c>: load, validate, <c>PreUpdate</c>, save and, when a scope is
    /// given, check in the database that the entity is still inside it.
    /// </summary>
    /// <param name="id">Entity identifier</param>
    /// <param name="entity">Updated object</param>
    /// <param name="scope">Scope the entity must still satisfy after the save, or null</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual async Task<TEntity> UpdateChangesCore(TKey id, TEntity entity,
        Expression<Func<TEntity, bool>>? scope, CancellationToken cancellationToken)
    {
        var includeProperties = GetIncludedProperties(entity, IgnoredProperties);
        var current = CurrentEntity != null && EqualityComparer<TKey>.Default.Equals(GetKey(CurrentEntity), id)
            ? CurrentEntity
            : await LoadRequiredAsync(id, cancellationToken, includeProperties);

        if (!IsValidToUpdate(current, out var errorMessage))
            throw new ApiException($"Failed to Update {typeof(TEntity).Name} '{id}'. {errorMessage}.", errorNumber: 4002, statusCode: HttpStatusCode.BadRequest);
        await PreUpdate(current, entity, cancellationToken);

        var updatedEntity = await CurrentRepository.UpdateAsync(current, cancellationToken) ?? current;
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        if (scope != null && !await StillInScopeAsync(id, scope, cancellationToken))
            throw NotFound(id);
        return updatedEntity;
    }

    private async Task<bool> StillInScopeAsync(TKey id, Expression<Func<TEntity, bool>> scope, CancellationToken cancellationToken) =>
        await CurrentRepository.AnyAsync(PredicateBuilder.New(ResolveKeyPredicate(id)).And(scope),
            QueryFlags.DisableTracking, cancellationToken);

    /// <summary>
    /// Update Entity property
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <param name="entity">The patch document</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<TEntity> Update(TKey id, JsonPatchDocument<TEntity> entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsurePatchable(entity);
        var includeProperties = GetIncludedProperties(entity, IgnoredProperties);
        var current = await LoadRequiredAsync(id, cancellationToken, includeProperties);

        if (!IsValidToUpdate(current, out var errorMessage))
            throw new ApiException($"Failed to Update {typeof(TEntity).Name} '{id}'. {errorMessage}.", errorNumber: 4002, statusCode: HttpStatusCode.BadRequest);
        await PreUpdate(current, entity, cancellationToken);
        entity.ApplyTo(current, error =>
        {
            if (error.Operation.OperationType != OperationType.Test &&
                error.Operation.OperationType != OperationType.Invalid)
            {
                HandleJsonOperator(error.Operation, error.AffectedObject);
            }
        });
        EnsureExtraPropertiesWithinLimits(current);
        var updatedEntity = await CurrentRepository.UpdateAsync(current, cancellationToken) ?? current;
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostUpdate(updatedEntity, cancellationToken);
        return updatedEntity;
    }

    /// <summary>
    /// Update if pass json object: only the properties present in the JSON change.
    /// </summary>
    /// <param name="id">Entity key</param>
    /// <param name="entity">Partial entity as JSON</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task<TEntity> Update(TKey id, JsonObject entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsurePatchable(entity);
        var includeProperties = GetIncludedProperties(entity, IgnoredProperties);
        var current = await LoadRequiredAsync(id, cancellationToken, includeProperties);
        if (!IsValidToUpdate(current, out var errorMessage))
            throw new ApiException($"Failed to Update {typeof(TEntity).Name} '{id}'. {errorMessage}.", errorNumber: 4002, statusCode: HttpStatusCode.BadRequest);
        await PreUpdate(current, entity, cancellationToken);
        entity.Populate(current, IsProtectedProperty);
        var updatedEntity = await CurrentRepository.UpdateAsync(current, cancellationToken) ?? current;
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostUpdate(updatedEntity, cancellationToken);
        return updatedEntity;
    }

    /// <summary>
    /// Validate if can update entity
    /// </summary>
    /// <param name="entity">The entity</param>
    /// <param name="validationErrorMessage">Reason when invalid</param>
    public virtual bool IsValidToUpdate(TEntity? entity, out string validationErrorMessage)
    {
        validationErrorMessage = string.Empty;
        return true;
    }

    /// <summary>Runs after an update was saved.</summary>
    /// <param name="entity">The updated entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual Task PostUpdate(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>Runs before an update with a full entity.</summary>
    /// <param name="current">The stored entity</param>
    /// <param name="entity">The new values</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual Task PreUpdate(TEntity current, TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Action can do before update entity
    /// </summary>
    /// <param name="current">current unchanged entity</param>
    /// <param name="entity">updated json entity</param>
    /// <param name="cancellationToken">cancellation token</param>
    public virtual Task PreUpdate(TEntity current, JsonObject entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Action can do before a JSON patch is applied
    /// </summary>
    /// <param name="current">The stored entity</param>
    /// <param name="entity">The patch document</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual Task PreUpdate(TEntity current, JsonPatchDocument<TEntity> entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    #endregion

    /// <summary>Whether a client may not change the property; override to protect more (a tenant column, for example).</summary>
    /// <param name="propertyName">Property name as sent by the client</param>
    protected virtual bool IsProtectedProperty(string propertyName) => EntityPropertyGuard.IsProtected(typeof(TEntity), propertyName);

    /// <inheritdoc />
    public virtual void EnsurePatchable(JsonPatchDocument<TEntity> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var operation in changes.Operations)
        {
            var value = operation.value is Newtonsoft.Json.Linq.JToken token ? token.ToClrValue() : operation.value;
            EnsurePathPatchable(operation.path, value);
            EnsurePathPatchable(operation.from, null);
        }
    }

    /// <inheritdoc />
    public virtual void EnsurePatchable(JsonObject changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var (name, _) in changes)
        {
            EnsureNamePatchable(name);
        }
    }

    /// <inheritdoc />
    public virtual void EnsureExtraPropertiesWithinLimits(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        EntityPropertyGuard.EnsureWithinLimits(entity.ExtraProperties);
    }

    /// <inheritdoc />
    public virtual void ApplyJson(JsonObject changes, TEntity entity) => changes.Populate(entity, IsProtectedProperty);

    // JSON pointer: segments are separated by '/', "~1" is a '/' and "~0" a '~' inside a segment
    private void EnsurePathPatchable(string? path, object? value)
    {
        var segments = path?.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Replace("~1", "/").Replace("~0", "~")).ToArray();
        if (segments is not { Length: > 0 })
        {
            return;
        }

        EnsureNamePatchable(segments[0]);
        if (segments[0].Equals(nameof(IEntity.ExtraProperties), StringComparison.OrdinalIgnoreCase)
            || EntityPropertyGuard.ResolveProperty(typeof(TEntity), segments[0])?.Name == nameof(IEntity.ExtraProperties))
        {
            // the bag is changed key by key, with the limits; replacing it as a whole would bypass them
            if (segments.Length == 1)
            {
                throw Protected(segments[0]);
            }

            if (value != null)
            {
                EntityPropertyGuard.EnsureExtraPropertyAllowed(new Dictionary<string, object?>(), segments[1], value);
            }
        }
    }

    // the protection rule runs on the CLR property the client name binds to, never on the client's spelling
    private void EnsureNamePatchable(string clientName)
    {
        var property = EntityPropertyGuard.ResolveProperty(typeof(TEntity), clientName);
        if (IsProtectedProperty(property?.Name ?? clientName))
        {
            throw Protected(property?.Name ?? clientName);
        }
    }

    private static ApiException Protected(string name) =>
        new($"The property '{name}' cannot be changed this way.", errorNumber: 4004, statusCode: HttpStatusCode.BadRequest, code: "PROTECTED_PROPERTY");

    /// <summary>
    /// Handle Json Patch Operation for a path that is not a property of the entity: the value goes to
    /// <see cref="IEntity.ExtraProperties"/> (add and replace assign the value, remove deletes it).
    /// </summary>
    /// <param name="op">The operation</param>
    /// <param name="targetObj">The object the operation was applied to</param>
    public virtual void HandleJsonOperator(Operation op, object? targetObj)
    {
        var currentObj = targetObj;

        var propPath = string.Empty;
        var propPaths = op.path.Split('/');
        var addToExtraProperties = false;
        for (var i = 1; i < propPaths.Length; i++)
        {
            propPath = propPaths[i];
            var props = currentObj?.GetType().GetProperties().Where(p => p.PropertyType.IsPublic);
            var propInfo = props?.FirstOrDefault(p => p.Name.Equals(propPath, StringComparison.CurrentCultureIgnoreCase));
            // check if propPath not available in object and children
            if (propInfo != null)
            {
                currentObj = propInfo.GetValue(currentObj);
            }
            else
            {
                addToExtraProperties = true;
                break;
            }
        }

        // if not available then check if object have attribute Json.. and IDictionary or just inherited from  IEntity
        if (addToExtraProperties && currentObj is IEntity entityObj)
        {
            var value = op.value is Newtonsoft.Json.Linq.JToken token ? token.ToClrValue() : op.value;
            if (op.OperationType is OperationType.Add or OperationType.Replace)
            {
                EntityPropertyGuard.EnsureExtraPropertyAllowed(entityObj.ExtraProperties, propPath, value);
            }

            switch (op.OperationType)
            {
                case OperationType.Add:
                case OperationType.Replace:
                    // RFC 6902: add replaces an existing member
                    entityObj.ExtraProperties[propPath] = value;
                    break;
                case OperationType.Remove:
                    entityObj.ExtraProperties.Remove(propPath);
                    break;
            }
        }
    }

    /// <summary>Navigation properties of the client entity that must be loaded before it is applied.</summary>
    /// <param name="fromClient">Entity sent by the client</param>
    /// <param name="ignoredProperties">Properties never included; null means none</param>
    protected virtual string[] GetIncludedProperties(TEntity fromClient, params string[]? ignoredProperties)
    {
        var properties = new List<string>();
        var entityType = typeof(TEntity);

        var entityProps = entityType.GetProperties().Where(prop =>
            prop.PropertyType.IsArray
            || (prop.PropertyType.IsGenericType &&
                prop.PropertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            || (prop.PropertyType.IsGenericType &&
                prop.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>))
            || (prop.PropertyType != typeof(string) && prop.PropertyType.IsClass &&
                prop.PropertyType.IsSubclassOf(typeof(Entity)))
        );
        ignoredProperties ??= Array.Empty<string>();

        foreach (var prop in entityProps)
        {
            var propValue = prop.GetValue(fromClient);
            if (propValue != null && !ignoredProperties.Contains(prop.Name))
            {
                properties.Add(prop.Name.FirstCharToUpper());
            }
        }

        return properties.ToArray();
    }

    /// <summary>Navigation properties present in the client JSON that must be loaded.</summary>
    /// <param name="fromClient">JSON sent by the client</param>
    /// <param name="ignoredProperties">Properties never included; null means none</param>
    protected virtual string[] GetIncludedProperties(JsonObject fromClient, params string[]? ignoredProperties)
    {
        var properties = GetJsonObjectProps(fromClient, "", GetProperties(typeof(TEntity)), ignoredProperties);
        return properties.ToArray();
    }

    /// <summary>Navigation properties named by the operations of a patch document.</summary>
    /// <param name="fromClient">Patch document sent by the client</param>
    /// <param name="ignoredProperties">Properties never included; null means none</param>
    protected virtual string[] GetIncludedProperties(JsonPatchDocument<TEntity> fromClient,
        params string[]? ignoredProperties)
    {
        var props = typeof(TEntity).GetProperties();
        var entityProps = props.Where(p => IsReferenceProperty(p.PropertyType)).Select(p => p.Name).ToList();
        return fromClient.Operations.SelectMany(o => GetPropertyPath(o.path, entityProps, ignoredProperties)).Distinct()
            .ToArray();
    }

    /// <summary>Include paths for a JSON patch path.</summary>
    /// <param name="path">The patch path</param>
    /// <param name="entityProps">Reference property names of the entity</param>
    /// <param name="ignoredProperties">Properties never included; null means none</param>
    protected virtual string[] GetPropertyPath(string path, IList<string> entityProps,
        params string[]? ignoredProperties)
    {
        var propPath = string.Empty;
        var paths = new List<string>();
        var props = path.Split('/');
        ignoredProperties ??= Array.Empty<string>();

        for (var i = 1; i < props.Length; i++)
        {
            var prop = props[i];
            if (prop.Length == 0) continue;
            prop = prop.FirstCharToUpper();
            if (!ignoredProperties.Contains(prop) && !int.TryParse(prop, out _) && entityProps.Contains(prop))
            {
                propPath += prop;
                paths.Add(propPath);
                if (i + 1 < props.Length)
                {
                    propPath += ".";
                }
            }
        }

        return paths.ToArray();
    }

    /// <summary>Reference properties of a property's type.</summary>
    /// <param name="parentProp">The property</param>
    protected virtual IList<PropertyInfo> GetProperties(PropertyInfo parentProp)
    {
        var props = parentProp.PropertyType.GetProperties();
        return props.Where(p => IsReferenceProperty(p.PropertyType)).ToList();
    }

    /// <summary>Reference properties of a type.</summary>
    /// <param name="parentType">The type</param>
    protected virtual IList<PropertyInfo> GetProperties(Type parentType)
    {
        var props = parentType.GetProperties();
        return props.Where(p => IsReferenceProperty(p.PropertyType)).ToList();
    }

    /// <summary>Include paths for the reference properties present in a JSON object.</summary>
    /// <param name="fromClient">The JSON object</param>
    /// <param name="parentPath">Path so far</param>
    /// <param name="entityProps">Reference properties at this level</param>
    /// <param name="ignoredProperties">Properties never included; null means none</param>
    protected virtual List<string> GetJsonObjectProps(JsonObject? fromClient, string parentPath,
        IList<PropertyInfo> entityProps, string[]? ignoredProperties = null)
    {
        var properties = new List<string>();
        if (fromClient == null) return properties;
        ignoredProperties ??= Array.Empty<string>();
        foreach (var (name, value) in fromClient.ToList())
        {
            if (name.Length == 0) continue;
            var jPropName = name.FirstCharToUpper();
            var path = string.IsNullOrEmpty(parentPath) ? jPropName : $"{parentPath}.{jPropName}";
            var objPropInfo =
                entityProps.FirstOrDefault(p => p.Name.Equals(jPropName, StringComparison.CurrentCultureIgnoreCase));
            if (objPropInfo == null || ignoredProperties.Contains(jPropName)) continue;

            if (value is JsonObject jsonObject)
            {
                properties.Add(path);
                properties.AddRange(GetJsonObjectProps(jsonObject, path, GetProperties(objPropInfo), ignoredProperties));
            }
            else if (value is JsonArray jArray)
            {
                properties.Add(path);
                foreach (var jToken in jArray)
                {
                    if (jToken is JsonObject token)
                    {
                        properties.AddRange(
                            GetJsonObjectProps(
                                token, path,
                                objPropInfo.PropertyType.IsConstructedGenericType
                                    ? GetProperties(objPropInfo.PropertyType.GenericTypeArguments.First())
                                    : new List<PropertyInfo>(),
                                ignoredProperties));
                    }
                }
            }
        }

        return properties;
    }

    /// <summary>Whether a type belongs to the same root namespace as the service (an entity reference).</summary>
    /// <param name="prop">The type</param>
    protected virtual bool IsReferenceProperty(Type prop)
    {
        var ns = GetType().Namespace;
        if (ns == null) return false;
        var currentNamespace = ns.Split('.').First();
        if (prop.IsConstructedGenericType &&
            prop.GenericTypeArguments.Any(g => g.Namespace?.StartsWith(currentNamespace, StringComparison.Ordinal) == true))
        {
            return true;
        }

        return prop.Namespace != null && prop.IsClass && prop.Namespace.StartsWith(currentNamespace, StringComparison.Ordinal);
    }

    /// <summary>Whether a type is a primitive-like value.</summary>
    /// <param name="type">The type</param>
    protected virtual bool IsPrimitive(Type type)
    {
        return type == typeof(string)
               || type == typeof(DateTime)
               || type == typeof(DateTime?)
               || type.IsPrimitive
               || (Nullable.GetUnderlyingType(type)?.IsPrimitive ?? false);
    }
}
