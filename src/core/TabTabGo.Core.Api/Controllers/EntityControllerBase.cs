using System.Text.Json.Nodes;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Extensions;
using TabTabGo.Core.Services;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Api.Controllers;

using System.Linq.Expressions;
using System.Net;
using TabTabGo.Core.Entities;

/// <summary>
/// base class to handle all the common stuff for the controllers
/// </summary>
/// <typeparam name="TEntity"></typeparam>
/// <typeparam name="TEntityService"></typeparam>
/// <typeparam name="TKey"></typeparam>
public class EntityControllerBase<TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest>(
    ILogger logger,
    IBaseService<TEntity, TKey> currentService,
    IMapper<TEntity, TViewModel>? mapper,
    IValidationService<TEntity, TCreateRequest>? validateService)
    : ControllerBase
    where TKey : IEquatable<TKey>
    where TEntity : class, IEntity
    where TViewModel : class
    //where TEntityService : IBaseService<TEntity, TKey>
    where TCreateRequest : class
    where TUpdateRequest : class
{
    // TODO create name in camelCase, separate by -
    // Get name from generic type name
    private string EntityName => typeof(TEntity).Name;

    protected readonly ILogger _logger = logger;

    //private readonly GenericServiceMapper<TEntity> _mapper;
    protected readonly IBaseService<TEntity, TKey> _currentService = currentService;
    protected readonly IMapper<TEntity, TViewModel>? _mapper = mapper;
    protected readonly IValidationService<TEntity, TCreateRequest>? _validateService = validateService;


    /// <summary>
    /// Get entities based on the given filter.
    /// </summary>
    /// <param name="query">OData query</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual async Task<IActionResult> SearchEntities(ODataQueryOptions<TEntity> query,
        CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("REST request to search {EntityName} on query {Query}", nameof(TEntity), @query);
        var entitiesResult = await _currentService.GetPageList(query, cancellationToken: cancellationToken);
        if (_mapper == null) return Ok(entitiesResult);
        var result = _mapper.MapPaging(entitiesResult);
        return Ok(result);
        //return Ok(_mapper.MapPaging<TResult>(result)).WithHeaders();
    }

    /// <summary>
    ///  Get Entity by id 
    /// </summary>
    /// <param name="id">Entity Identification</param>
    /// <param name="expand"></param>
    /// <param name="cancellationToken"></param>
    /// <param name="lastUpdatedDate"></param>
    /// <returns>Entity</returns>
    protected virtual async Task<IActionResult> GetEntity([FromRoute] TKey id,
        [FromQuery] DateTimeOffset? lastUpdatedDate = null, [FromQuery] string? expand = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to get {EntityName} : {Id}", nameof(TEntity), id);
        try
        {
            if (!await _currentService.Exists(id, cancellationToken))
            {
                return NotFound(new
                {
                    Message = $"{nameof(TEntity)} not found for passed {id}",
                    Code = $"{nameof(TEntity)}_Not_Found".ToUpper()
                });
            }

            var detail = await _currentService.GetByKey(id, lastUpdatedDate, expand?.Split(','),
                cancellationToken: cancellationToken);
            //  var entityViewModel = _mapper.Map<Entity>(detail);
            return Ok(detail);
        }
        catch (ApiException ex)
        {
            return StatusCode((int)ex.HttpStatusCode, new { Message = ex.Message, Code = ex.ErrorCode, });
        }
    }

    /// <summary>
    ///  Export list of Entity
    /// </summary>
    /// <param name="format">Export type</param>
    /// <param name="query"></param>
    /// <param name="export"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Exported Document</returns>
    protected async virtual Task<IActionResult> ExportEntities([FromRoute] ExportType format,
        ODataQueryOptions<TEntity> query, [FromBody] ExportConfiguration export = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to export ${EntityName} to type {ExportType}", nameof(TEntity), format);
        export ??= new ExportConfiguration();
        if (string.IsNullOrEmpty(export.FileName))
        {
            var preffix = nameof(EntityName);
            // add- if name have Upper case letter

            export.FileName = $"{preffix}_{DateTime.UtcNow:yyyyMMdd}.{format.FileExtenstion()}";
        }

        export.FileType = format;
        var stream = await _currentService.ExportFile<object>(export, query, cancellationToken: cancellationToken);

        return File(stream, "application/octet-stream", export.FileName);
    }

    /// <summary>
    ///  Export list of Entity
    /// </summary>
    /// <param name="format">Export type</param>
    /// <param name="fixCriteria"></param>
    /// <param name="query"></param>
    /// <param name="export"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Exported Document</returns>
    protected async virtual Task<IActionResult> ExportEntities([FromRoute] ExportType format,
        ODataQueryOptions<TEntity> query,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        [FromBody] ExportConfiguration? export = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to export ${EntityName} to type {ExportType}", nameof(TEntity), format);
        export ??= new ExportConfiguration();
        if (string.IsNullOrEmpty(export.FileName))
        {
            var preffix = EntityName;
            // add- if name have Upper case letter
            export.FileName = $"{preffix}_{DateTime.UtcNow:yyyyMMdd}.{format.FileExtenstion()}";
        }

        export.FileType = format;
        var stream = await _currentService.ExportFile<object>(export, query, fixCriteria: fixCriteria,
            cancellationToken: cancellationToken);

        return File(stream, "application/octet-stream", export.FileName);
    }

    /// <summary>
    /// Create Entity 
    /// </summary>
    /// <param name="requestedEntity">new Entity to be created</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Created Entity</returns>
    protected virtual async Task<IActionResult> CreateEntity([FromBody] TCreateRequest requestedEntity,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating a new {EntityName} with request {@Request}", EntityName, requestedEntity);
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var validationResult = validateService != null
                ? await validateService.Validate(requestedEntity, cancellationToken)
                : null;
            if (validationResult is { IsValid: false })
            {
                return BadRequest(new
                {
                    Message = "Invalid update request",
                    Code = $"{EntityName}_Invalid_Update_Request".ToUpper(),
                    Errors = validationResult.Errors,
                });
            }

            logger.LogInformation("Validation passed for {EntityName}", EntityName);
            var entity = mapper != null ? mapper.MapFromRequest(requestedEntity) : requestedEntity as TEntity;
            var createdEntity = await currentService.Create(entity, cancellationToken);
            var id = currentService.GetKey(createdEntity);
            logger.LogInformation("Created {EntityName} with id {Id}", EntityName, id);
            var entityViewModel = mapper?.MapToViewModel(createdEntity);

            return Created(EntityName, entityViewModel != null ? entityViewModel : createdEntity);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to create {EntityName} : {@EntityRequest}", EntityName, @requestedEntity);
            return StatusCode((int)ex.HttpStatusCode, new { Message = ex.Message, Code = ex.ErrorCode, });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create {EntityName} : {@EntityRequest}", EntityName, @requestedEntity);
            return StatusCode((int)HttpStatusCode.InternalServerError, new
            {
                Message = $"Failed to create {EntityName}.", Code = $"Create_{EntityName}_Failed".ToUpper(),
                exception = e
            });
        }
    }

    /// <summary>
    /// Update in patch
    /// </summary>
    /// <param name="id"></param>
    /// <param name="changes"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<(IActionResult?, TEntity?)> InternalPatchEntity(TKey id,
        JsonPatchDocument<TEntity> changes, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("REST request to patch ${EntityName} : {@Changes}", EntityName, @changes);

        if (id.Equals(0))
            return (BadRequest(new
            {
                Message = "Invalid Id", Errors = new Dictionary<string, string>()
                {
                    { "Id", "Invalid Id" }
                }
            }), null);

        if (!ModelState.IsValid)
        {
            return (BadRequest(ModelState), null);
        }

        var entity = await _currentService.GetByKey(id, cancellationToken: cancellationToken);
        if (entity == null)
        {
            return (
                NotFound(new
                {
                    Message = $"{EntityName} not found for passed {id}", Code = $"{EntityName}_Not_Found".ToUpper()
                }), null);
        }
        
        changes.ApplyTo(entity, error =>
        {
            if (error.Operation.OperationType != OperationType.Test &&
                error.Operation.OperationType != OperationType.Invalid)
            {
                _currentService.HandleJsonOperator(error.Operation, error.AffectedObject);
            }
        });

        var validationResult = _validateService != null ? await _validateService.Validate(entity, cancellationToken) : null;
        if (validationResult is { IsValid: true })
        {
            return (
                BadRequest(new
                {
                    Message = "Invalid patch request",
                    Code = "Invalid_Patch_Request".ToUpper(),
                    Errors = validationResult.Errors,
                }), null);
        }

        try
        {
            await _currentService.UpdateChanges(id, entity, cancellationToken);
            
            return  (null , entity);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to update {EntityName}", EntityName);
            return (StatusCode((int)ex.HttpStatusCode, new { Message = ex.Message, Code = ex.ErrorCode, }), null);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create {EntityName}", EntityName);
            return (
                StatusCode(500, new { Message = e.Message, Code = "Update_Entity_Failed".ToUpper(), exception = e }),
                null);
        }
    }

    /// <summary>
    /// Partially update Entity using patch document
    /// </summary>
    /// <param name="id">Entity identifier</param>
    /// <param name=""></param>
    /// <param name="changes"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Updated Entity</returns>
    protected virtual async Task<IActionResult> PatchEntity([FromRoute] TKey id,
        [FromBody] JsonPatchDocument<TEntity> changes, CancellationToken cancellationToken = default)
    {
        var (response, entity) = await InternalPatchEntity(id, changes, cancellationToken);
        if (response != null)
            return response;
        if(_mapper == null )
            return Ok(entity);
        var updatedEntity = _mapper.MapToViewModel(entity);
        return Ok(updatedEntity);
    }
   
    /// <summary>
    /// Partially update Entity using json document
    /// </summary>
    /// <param name="id">Entity identifier</param>
    /// <param name=""></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Updated Entity</returns>
    protected virtual async Task<IActionResult> PatchEntityByJson([FromRoute] TKey id, [FromBody] JsonObject changes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to patch ${EntityName} : {Changes}", EntityName, changes);

        // TODO check if id is null or empty
        if (id.Equals(0)) return BadRequest(new { Message = "Invalid Id", Errors = new Dictionary<string, string>() { { "Id", "Invalid Id" } } });

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (!await _currentService.Exists(id, cancellationToken))
        {
            return NotFound(new
            {
                Message = $"{EntityName} not found for passed {id}", Code = $"{EntityName}_Not_Found".ToUpper()
            });
        }

        var entity = await _currentService.GetByKey(id, cancellationToken: cancellationToken);
        //var entityViewModel = _mapper.Map<Entity>(entity);

        changes.Populate(entity);

        var validationResult = await _validateService.Validate(entity, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(new
            {
                Message = "Invalid patch request",
                Code = "Invalid_Patch_Request",
                Errors = validationResult.Errors,
            });
        }

        try
        {
            //var updatedEntity = _mapper.Map<Entity>(entityViewModel);

            await _currentService.UpdateChanges(id, entity, cancellationToken);

            return Ok(entity);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to update {EntityName} with id : {Id}", EntityName, id);
            return StatusCode((int)ex.HttpStatusCode, new { Message = ex.Message, Code = ex.ErrorCode, });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update {EntityName} with id : {Id}", EntityName, id);
            return StatusCode(500, new { Message = e.Message, Code = "Update_Entity_Failed".ToUpper(), exception = e });
        }
    }

    /// <summary>
    /// Delete Entities (soft Delete)
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual async Task<IActionResult> DeleteEntity([FromRoute] TKey id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to delete ${EntityName} : {Id}", EntityName, id);

        if (!await _currentService.Exists(id, cancellationToken))
        {
            return NotFound(new
            {
                Message = $"{EntityName} not found for passed {id}", Code = $"{EntityName}_Not_Found".ToUpper()
            });
        }

        if (await _currentService.CanDelete(id, cancellationToken))
        {
            try
            {
                var deletedInstance = await _currentService.Delete(id, cancellationToken);
                return Ok(new
                {
                    Message = $"{EntityName} deleted successfully", Code = $"{EntityName}_Deleted".ToUpper()
                });
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Failed to delete {EntityName}", EntityName);
                return StatusCode((int)ex.HttpStatusCode, new { Message = ex.Message, Code = ex.ErrorCode, });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to delete {EntityName}", EntityName);
                return StatusCode(500,
                    new { Message = e.Message, Code = "Delete_Entity_Failed".ToUpper(), exception = e });
            }
        }

        return StatusCode(412,
            new { Message = $"{EntityName} #{id} can't be deleted", Code = $"{EntityName}_Cannot_Delete".ToUpper() });
    }

    /// <summary>
    /// Delete Entity (soft Delete)
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ids"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual async Task<IActionResult> DeleteEntities([FromBody] IEnumerable<TKey> ids,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"REST request to delete list of {EntityName} : {ids}");
        if (!await _currentService.CanDelete(ids, cancellationToken))
            return StatusCode(412,
                new { Message = $"{EntityName} can't be deleted", Code = $"{EntityName}_Cannot_Delete".ToUpper() });
        try
        {
            var deletedInstance = await _currentService.Delete(ids, cancellationToken);
            return Ok(new { Message = $"{EntityName} deleted successfully", Code = $"{EntityName}_Deleted".ToUpper() });
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to delete list of {EntityName}", EntityName);
            return StatusCode((int)ex.HttpStatusCode, new { Message = ex.Message, Code = ex.ErrorCode, });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete list of {EntityName}, Ids {@Ids}", EntityName, ids);
            return StatusCode(500,
                new { Message = e.Message, Code = "Delete_Entity_Failed".ToUpper(), exception = e });
        }
    }
}