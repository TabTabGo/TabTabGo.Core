using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Extensions;
using Microsoft.AspNetCore.JsonPatch;
using System.Reflection;
using System.Text.Json.Nodes;
using LinqKit;
using Microsoft.AspNetCore.JsonPatch.Operations;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Infrastructure.Data;
using TabTabGo.Core.ViewModels;
namespace TabTabGo.Core.Services;

[SuppressMessage("ReSharper", "MemberCanBeProtected.Global")]
public abstract class BaseService<TEntity, TKey> : BaseReadService<TEntity, TKey>, IBaseService<TEntity, TKey>
    where TEntity : class, IEntity
{
    protected readonly IUnitOfWork _unitOfWork;

    public new IGenericRepository<TEntity, TKey> CurrentRepository =>
        (IGenericRepository<TEntity, TKey>)base.CurrentRepository;

    public BaseService(IUnitOfWork unitOfWork, IGenericRepository<TEntity, TKey> repository,
        ILogger<BaseService<TEntity, TKey>> logger) : base(repository, logger)
    {
        _unitOfWork = unitOfWork;
    }
    
    public BaseService(IUnitOfWork unitOfWork,
        ILogger<BaseService<TEntity, TKey>> logger) : base(unitOfWork.Repository<TEntity,TKey>(), logger)
    {
        _unitOfWork = unitOfWork;
    }

    #region Create Methods

    public virtual void HandleJsonOperator<T>(Operation operation, object affectedObject)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Create Entity
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<TEntity> Create(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidToCreate(entity, out string errorMessage))
            throw new ApiException($"Failed to create {entity.ToString()}. {errorMessage}.", errorNumber: 2001);
        await PreCreate(entity, cancellationToken);
        var newEntity = await CurrentRepository.InsertAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await LoadEntityAsync(GetKey(entity), cancellationToken);
        await PostCreate(CurrentEntity, cancellationToken);
        return CurrentEntity;
    }

    /// <summary>
    /// Validate passed entity before createed in DB
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="validationErrorMessage"></param>
    /// <returns></returns>
    public virtual bool IsValidToCreate(TEntity entity, out string validationErrorMessage)
    {
        validationErrorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Post Create methods
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual Task PostCreate(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

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
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<TEntity> Delete(TKey id, CancellationToken cancellationToken = default)
    {
        await LoadEntityAsync(id, cancellationToken: cancellationToken);

        if (!IsValidToDelete(CurrentEntity, out string errorMessage))
            throw new Exception($"Can't delete {CurrentEntity.ToString()}. {errorMessage}");
        await PreDelete(this.CurrentEntity, cancellationToken);
        CurrentEntity.IsEnabled = false;
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostDelete(this.CurrentEntity, cancellationToken);
        return CurrentEntity;
    }

    /// <summary>
    /// Validate if entity can be deleted 
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="validationErrorMessage"></param>
    /// <returns></returns>
    public virtual bool IsValidToDelete(TEntity entity, out string validationErrorMessage)
    {
        validationErrorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// function executed after the delete eneityt is completed 
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual Task PostDelete(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected virtual Task PreDelete(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected virtual Task PostDelete(IEnumerable<TEntity> entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected virtual async Task PreDelete(IEnumerable<TEntity> entity,
        CancellationToken cancellationToken = default)
    {
    }

    /// <summary>
    /// Function used to check if can delete entity from client side 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<bool> CanDelete(TKey id, CancellationToken cancellationToken = default)
    {
        await LoadEntityAsync(id, cancellationToken);
        return IsValidToDelete(CurrentEntity, out string errorMessage);
    }

    public virtual Task<Stream> ExportFile<TResult>(ExportConfiguration config, object oDataQueryOptions,
        Expression<Func<TEntity, bool>> fixCriteria = null,
        string[] includeProperties = null, CancellationToken cancellationToken = default)
        where TResult : class
    {
        throw new NotImplementedException();
    }

    public virtual async Task<bool> CanDelete(IEnumerable<TKey> ids, CancellationToken cancellationToken)
    {
        var entities = await CurrentRepository.GetAsync(filter: e => ids.Contains(GetKey(e)),
            cancellationToken: cancellationToken);
        var errors = new Dictionary<TKey, (bool CanDelete, string Error)>();
        string currentErrorMessage;
        bool canDelete = true;
        foreach (var entity in entities)
        {
            canDelete = IsValidToDelete(entity, out currentErrorMessage);
            errors.Add(GetKey(entity), (canDelete, currentErrorMessage));
        }

        return true;
    }

    public virtual async Task<IEnumerable<TEntity>> Delete(IEnumerable<TKey> ids, CancellationToken cancellationToken)
    {
        var entities = await CurrentRepository.GetAsync(filter: e => ids.Contains(GetKey(e)),
            cancellationToken: cancellationToken);
        var enumerable = entities as TEntity[] ?? entities.ToArray();
        await PreDelete(enumerable, cancellationToken);
        foreach (var entity in enumerable)
        {
            entity.IsEnabled = false;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await PostDelete(enumerable, cancellationToken);
        return enumerable;
    }

    #endregion

    #region Update Methods

    /// <summary>
    ///  Update Entity 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="entity"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<TEntity> Update(TKey id, TEntity entity,
        CancellationToken cancellationToken = default)
    {
        var includeProperties = GetIncludedProperties(entity, IgnoredProperties);
        await LoadEntityAsync(id, cancellationToken, includeProperties);
        //AutoMapper.Mapper.Map<TEntity, TEntity>(entity, this.CurrentEntity);
        if (!IsValidToUpdate(entity, out string errorMessage))
            throw new ApiException($"Failed to Update {CurrentEntity.ToString()}. {errorMessage}", errorNumber: 4001);
        await PreUpdate(CurrentEntity, entity, cancellationToken);
        entity.CopyObject<TEntity>(CurrentEntity, "IsEnabled");
        //var updatedEntity = await CurrentRepository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostUpdate(CurrentEntity, cancellationToken);
        return CurrentEntity;
    }

    /// <summary>
    /// Update entity after process patch json on entity object
    /// </summary>
    /// <param name="id">entity identifier</param>
    /// <param name="entity">updated object</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns></returns>
    /// <exception cref="ApiException"></exception>
    public virtual async Task<TEntity> UpdateChanges(TKey id, TEntity entity, CancellationToken cancellationToken)
    {
        var includeProperties = GetIncludedProperties(entity, IgnoredProperties);
        if(CurrentEntity == null || !GetKeyPredicate(id).Invoke(CurrentEntity))
            await LoadEntityAsync(id, cancellationToken, includeProperties);
        
        if (!IsValidToUpdate(CurrentEntity, out string errorMessage))
            throw new ApiException($"Failed to Update {CurrentEntity.ToString()}. {errorMessage}.", errorNumber: 4002);
        await PreUpdate(CurrentEntity, entity, cancellationToken);
        
        var updatedEntity = await CurrentRepository.UpdateAsync(CurrentEntity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostUpdate(updatedEntity, cancellationToken);
        return updatedEntity;
    }

    /// <summary>
    /// Update Entity property 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="entity"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<TEntity> Update(TKey id, JsonPatchDocument<TEntity> entity,
        CancellationToken cancellationToken = default)
    {
        var includeProperties = GetIncludedProperties(entity, IgnoredProperties);
        await LoadEntityAsync(id, cancellationToken, includeProperties);

        if (!IsValidToUpdate(CurrentEntity, out string errorMessage))
            throw new ApiException($"Failed to Update {CurrentEntity.ToString()}. {errorMessage}.", errorNumber: 4002);
        await PreUpdate(CurrentEntity, entity, cancellationToken);
        entity.ApplyTo(CurrentEntity, error =>
        {
            if (error.Operation.OperationType != OperationType.Test &&
                error.Operation.OperationType != OperationType.Invalid)
            {
                this.HandleJsonOperator(error.Operation, error.AffectedObject);
            }
        });
        var updatedEntity = await CurrentRepository.UpdateAsync(CurrentEntity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostUpdate(updatedEntity, cancellationToken);
        return updatedEntity;
    }


    /// <summary>
    /// Update if pass json object 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="entity"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<TEntity> Update(TKey id, JsonObject entity,
        CancellationToken cancellationToken = default)
    {
        var includeProeprties = GetIncludedProperties(entity, IgnoredProperties);
        await LoadEntityAsync(id, cancellationToken, includeProeprties);
        if (!IsValidToUpdate(CurrentEntity, out string errorMessage))
            throw new ApiException($"Failed to Update {CurrentEntity.ToString()}. {errorMessage}.", errorNumber: 4002);
        await PreUpdate(CurrentEntity, entity, cancellationToken);
        entity.Populate<TEntity>(CurrentEntity);
        var updatedEntity = await CurrentRepository.UpdateAsync(CurrentEntity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken: cancellationToken);
        await PostUpdate(updatedEntity, cancellationToken);
        return updatedEntity;
    }

    /// <summary>
    /// Validate if can update entity 
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="validationErrorMessage"></param>
    /// <returns></returns>
    public virtual bool IsValidToUpdate(TEntity? entity, out string validationErrorMessage)
    {
        validationErrorMessage = string.Empty;
        return true;
    }

    public virtual Task PostUpdate(TEntity entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

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
    /// <returns></returns>
    public virtual Task PreUpdate(TEntity current, JsonObject entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="current"></param>
    /// <param name="entity"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual Task PreUpdate(TEntity current, JsonPatchDocument<TEntity> entity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// Handle Json Patch Operation
    /// </summary>
    /// <param name="op"></param>
    /// <param name="targetObj"></param>
    public virtual void HandleJsonOperator(Operation op, object? targetObj)
    {
        object? currentObj = targetObj;

        string propPath = string.Empty;
        var propPaths = op.path.Split('/');
        var addToExtraProperties = false;
        for (int i = 1; i < propPaths.Length; i++)
        {
            //TODO can be enhanced
            propPath = propPaths[i];
            // TODO Check if the path is number then get index
            IEnumerable<PropertyInfo?> props = currentObj?.GetType()?.GetProperties()?.Where(p => p.PropertyType.IsPublic);
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
        if (addToExtraProperties && currentObj is IEntity)
        {
            var entityObj = (IEntity)currentObj;
            // then process operation based on type
            switch (op.OperationType)
            {
                case OperationType.Add:
                    entityObj.ExtraProperties.Add(propPath, op.value);
                    break;
                case OperationType.Remove:
                    if (entityObj.ExtraProperties.ContainsKey(propPath))
                    {
                        entityObj.ExtraProperties.Remove(propPath);
                    }

                    break;
                case OperationType.Replace:
                    if (entityObj.ExtraProperties.ContainsKey(propPath))
                    {
                        entityObj.ExtraProperties[propPath] = op.value;
                    }
                    else
                    {
                        entityObj.ExtraProperties.Add(propPath, op.value);
                    }

                    break;
            }
        }
    }

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
        if (ignoredProperties == null)
            ignoredProperties = new string[] { };

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

    protected virtual string[] GetIncludedProperties(JsonObject fromClient, params string[]? ignoredProperties)
    {
        var properties = GetJsonObjectProps(fromClient, "", GetProperties(typeof(TEntity)), ignoredProperties);
        return properties.ToArray();
    }

    protected virtual string[] GetIncludedProperties(JsonPatchDocument<TEntity> fromClient,
        params string[]? ignoredProperties)
    {
        var props = typeof(TEntity).GetProperties();
        var entityProps = props.Where(p => IsReferenceProperty(p.PropertyType)).Select(p => p.Name).ToList();
        return fromClient.Operations.SelectMany(o => GetPropertyPath(o.path, entityProps, ignoredProperties)).Distinct()
            .ToArray();
    }

    protected virtual string[] GetPropertyPath(string path, IList<string> entityProps,
        params string[]? ignoredProperties)
    {
        var propPath = string.Empty;
        var paths = new List<string>();
        var props = path.Split('/');

        for (int i = 1; i < props.Length; i++)
        {
            var prop = props[i];
            prop = prop.First().ToString().ToUpper() + prop.Substring(1);
            if (ignoredProperties != null && !ignoredProperties.Contains(prop) && !int.TryParse(prop, out int dummy) && entityProps.Contains(prop))
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

    protected virtual IList<PropertyInfo> GetProperties(PropertyInfo parentProp)
    {
        var props = parentProp.PropertyType.GetProperties();
        return props.Where(p => IsReferenceProperty(p.PropertyType)).ToList();
    }

    protected virtual IList<PropertyInfo> GetProperties(Type parentType)
    {
        var props = parentType.GetProperties();
        return props.Where(p => IsReferenceProperty(p.PropertyType)).ToList();
    }

    protected virtual List<string> GetJsonObjectProps(JsonObject? fromClient, string parentPath,
        IList<PropertyInfo> entityProps, string[]? ignoredProperties = null)
    {
        var properties = new List<string>();
        if (fromClient == null) return properties;
        // should filter by entity type too
        var jObjProps = fromClient.ToList();
        KeyValuePair<string, JsonNode> jObjProp;
        string jPropName;
        string path;
        PropertyInfo? objPropInfo;
        foreach (var t in jObjProps)
        {
            jObjProp = t;
            jPropName = jObjProp.Key.First().ToString().ToUpper() + jObjProp.Key.Substring(1);
            path = string.IsNullOrEmpty(parentPath) ? jPropName : $"{parentPath}.{jPropName}";
            objPropInfo =
                entityProps.FirstOrDefault(p => p.Name.Equals(jPropName, StringComparison.CurrentCultureIgnoreCase));
            if (
                (jObjProp.Value is JsonObject || jObjProp.Value is JsonArray)
                && objPropInfo != null
                && ignoredProperties != null &&!ignoredProperties.Contains(jPropName))
            {
                if (jObjProp.Value is JsonObject)
                {
                    properties.Add(path);

                    properties.AddRange(GetJsonObjectProps(jObjProp.Value as JsonObject, path, GetProperties(objPropInfo),
                        ignoredProperties));
                }
                else if (jObjProp.Value is JsonArray)
                {
                    properties.Add(path);
                    var jArray = jObjProp.Value as JsonArray;

                    if (jArray != null)
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
                                        ignoredProperties)
                                );
                            }

                            //TODO handle case array in array
                        }
                }
            }
        }

        return properties;
    }

    protected virtual bool IsReferenceProperty(Type prop)
    {
        var ns = this.GetType().Namespace;
        if (ns == null) return false;
        var currentNamespace = ns.Split('.').First();
        if (prop.IsConstructedGenericType &&
            prop.GenericTypeArguments.Any(g => g.Namespace.StartsWith(currentNamespace)))
        {
            return true;
        }

        if (prop.Namespace == null || !prop.IsClass || (!prop.Namespace.StartsWith(currentNamespace))) return false;
        return true;
    }

    protected virtual bool IsPrimitive(Type type)
    {
        return type == typeof(string)
               || type == typeof(DateTime)
               || type == typeof(DateTime?)
               || type.IsPrimitive
               || (Nullable.GetUnderlyingType(type) != null && Nullable.GetUnderlyingType(type).IsPrimitive);
    }
}