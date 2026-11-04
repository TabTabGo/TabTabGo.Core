using System.Linq.Expressions;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Api.Errors;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Extensions;
using TabTabGo.Core.Services;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Api.Controllers;

/// <summary>
/// Base class to handle all the common stuff for the MVC controllers. Legacy: new code should use
/// <see cref="SetupEntityRouters.MapEntityRoutes{TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest}(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder, string, Action{EntityRouterOptions{TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest}}?)"/>.
/// Override <see cref="SearchFixCriteria"/>, <see cref="GetFixCriteria"/> and <see cref="ExportFixCriteria"/> to scope what a caller can reach (tenant, owner...).
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TKey">Entity key type</typeparam>
/// <typeparam name="TViewModel">View model type</typeparam>
/// <typeparam name="TCreateRequest">Create request type</typeparam>
/// <typeparam name="TUpdateRequest">Update request type</typeparam>
public class EntityControllerBase<TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest> : ControllerBase
    where TKey : IEquatable<TKey>
    where TEntity : class, IEntity
    where TViewModel : class
    where TCreateRequest : class
    where TUpdateRequest : class
{
    private string EntityName => typeof(TEntity).Name;

    /// <summary>Logger.</summary>
    protected readonly ILogger _logger;

    /// <summary>The entity service.</summary>
    protected readonly IBaseService<TEntity, TKey> _currentService;

    /// <summary>Maps entities to view models; null when the entity is the view model.</summary>
    protected readonly IMapper<TEntity, TViewModel>? _mapper;

    /// <summary>Validates create requests and patched entities; null skips validation.</summary>
    protected readonly IValidationService<TEntity, TCreateRequest>? _validateService;

    /// <summary>Creates the controller.</summary>
    /// <param name="logger">Logger</param>
    /// <param name="currentService">Entity service</param>
    /// <param name="mapper">Optional mapper</param>
    /// <param name="validateService">Optional validation service</param>
    public EntityControllerBase(
        ILogger logger,
        IBaseService<TEntity, TKey> currentService,
        IMapper<TEntity, TViewModel>? mapper,
        IValidationService<TEntity, TCreateRequest>? validateService)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(currentService);
        _logger = logger;
        _currentService = currentService;
        _mapper = mapper;
        _validateService = validateService;
    }

    /// <summary>Scope criteria always applied to search. Null (default) means no scope.</summary>
    protected virtual Expression<Func<TEntity, bool>>? SearchFixCriteria => null;

    /// <summary>
    /// Scope criteria always applied when an entity is looked up by key: get, patch and delete. An entity outside the
    /// scope answers 404. Null (default) means no scope.
    /// </summary>
    protected virtual Expression<Func<TEntity, bool>>? GetFixCriteria => null;

    /// <summary>Scope criteria always applied to export. Null (default) means no scope.</summary>
    protected virtual Expression<Func<TEntity, bool>>? ExportFixCriteria => null;

    private ObjectResult Error(HttpStatusCode status, ApiErrorBody body) => StatusCode((int)status, body);

    private ObjectResult Error(ApiException exception) => Error(exception.HttpStatusCode, ApiErrorResponses.From(exception, HttpContext));

    private IActionResult NotFoundEntity(TKey id) => NotFound(ApiErrorResponses.NotFound(EntityName, id, HttpContext));

    private IActionResult BadRequestBody(string message, string code, ValidationResult? validationResult = null) =>
        BadRequest(ApiErrorResponses.Validation(message, code, validationResult, HttpContext));

    /// <summary>
    /// Get entities based on the given filter.
    /// </summary>
    /// <param name="query">OData query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual async Task<IActionResult> SearchEntities(ODataQueryOptions<TEntity> query,
        CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("REST request to search {EntityName}", EntityName);
        try
        {
            var entitiesResult = await _currentService.GetPageList(query, SearchFixCriteria, cancellationToken: cancellationToken);
            if (_mapper == null) return Ok(entitiesResult);
            return Ok(_mapper.MapPaging(entitiesResult));
        }
        catch (ApiException ex)
        {
            return Error(ex);
        }
    }

    /// <summary>
    ///  Get Entity by id
    /// </summary>
    /// <param name="id">Entity Identification</param>
    /// <param name="expand">Comma separated properties to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="lastUpdatedDate">Only entities last updated before this date are returned</param>
    /// <returns>Entity</returns>
    protected virtual async Task<IActionResult> GetEntity([FromRoute] TKey id,
        [FromQuery] DateTimeOffset? lastUpdatedDate = null, [FromQuery] string? expand = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to get {EntityName} : {Id}", EntityName, id);
        try
        {
            var detail = await _currentService.GetByKey(id, lastUpdatedDate, expand?.Split(','),
                fixCriteria: GetFixCriteria, cancellationToken: cancellationToken);
            if (detail == null)
            {
                return NotFoundEntity(id);
            }

            return Ok(detail);
        }
        catch (ApiException ex)
        {
            return Error(ex);
        }
    }

    /// <summary>
    ///  Export list of Entity
    /// </summary>
    /// <param name="format">Export type</param>
    /// <param name="query">OData query</param>
    /// <param name="export">Export options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Exported Document</returns>
    protected virtual Task<IActionResult> ExportEntities([FromRoute] ExportType format,
        ODataQueryOptions<TEntity> query, [FromBody] ExportConfiguration? export = null,
        CancellationToken cancellationToken = default)
        => ExportEntities(format, query, ExportFixCriteria, export, cancellationToken);

    /// <summary>
    ///  Export list of Entity
    /// </summary>
    /// <param name="format">Export type</param>
    /// <param name="fixCriteria">Scope criteria</param>
    /// <param name="query">OData query</param>
    /// <param name="export">Export options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Exported Document</returns>
    protected virtual async Task<IActionResult> ExportEntities([FromRoute] ExportType format,
        ODataQueryOptions<TEntity> query,
        Expression<Func<TEntity, bool>>? fixCriteria = null,
        [FromBody] ExportConfiguration? export = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to export {EntityName} to type {ExportType}", EntityName, format);
        export ??= new ExportConfiguration();
        if (string.IsNullOrEmpty(export.FileName))
        {
            // FileExtenstion already starts with the dot
            export.FileName = $"{EntityName}_{DateTime.UtcNow:yyyyMMdd}{format.FileExtenstion()}";
        }

        export.FileType = format;
        try
        {
            var stream = await _currentService.ExportFile<object>(export, query, fixCriteria: fixCriteria,
                cancellationToken: cancellationToken);

            return File(stream, "application/octet-stream", export.FileName);
        }
        catch (NotImplementedException)
        {
            return Error(HttpStatusCode.NotImplemented,
                ApiErrorResponses.Failure($"{EntityName} export is not implemented", $"{EntityName}_Export_Not_Implemented".ToUpperInvariant(), HttpContext));
        }
        catch (ApiException ex)
        {
            return Error(ex);
        }
    }

    /// <summary>
    /// Create Entity
    /// </summary>
    /// <param name="requestedEntity">new Entity to be created</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created Entity</returns>
    protected virtual async Task<IActionResult> CreateEntity([FromBody] TCreateRequest requestedEntity,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating a new {EntityName}", EntityName);
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var validationResult = _validateService != null
                ? await _validateService.Validate(requestedEntity, cancellationToken)
                : null;
            if (validationResult is { IsValid: false })
            {
                return BadRequestBody("Invalid create request", $"{EntityName}_Invalid_Create_Request".ToUpperInvariant(), validationResult);
            }

            if (_mapper == null && requestedEntity is not TEntity)
            {
                // only create needs the mapper; every other action works without one
                _logger.LogError("No IMapper is registered and {Request} is not a {EntityName}", typeof(TCreateRequest).Name, EntityName);
                return Error(HttpStatusCode.InternalServerError, ApiErrorResponses.Failure(
                    $"Creating {EntityName} needs an IMapper<{EntityName}, {typeof(TViewModel).Name}>: {typeof(TCreateRequest).Name} is not a {EntityName}.",
                    "MAPPER_REQUIRED", HttpContext));
            }

            _logger.LogInformation("Validation passed for {EntityName}", EntityName);
            var entity = _mapper != null ? _mapper.MapFromRequest(requestedEntity) : requestedEntity as TEntity;
            var createdEntity = await _currentService.Create(entity, cancellationToken);
            var id = _currentService.GetKey(createdEntity);
            _logger.LogInformation("Created {EntityName} with id {Id}", EntityName, id);
            var entityViewModel = _mapper?.MapToViewModel(createdEntity);

            return Created($"{Request.Path.Value?.TrimEnd('/')}/{id}", entityViewModel != null ? entityViewModel : createdEntity);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to create {EntityName}", EntityName);
            return Error(ex);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create {EntityName}", EntityName);
            return Error(HttpStatusCode.InternalServerError,
                ApiErrorResponses.Failure($"Failed to create {EntityName}.", $"Create_{EntityName}_Failed".ToUpperInvariant(), HttpContext));
        }
    }

    /// <summary>
    /// Update in patch
    /// </summary>
    private async Task<(IActionResult?, TEntity?)> InternalPatchEntity(TKey id,
        JsonPatchDocument<TEntity> changes, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("REST request to patch {EntityName}", EntityName);

        if (IsDefaultKey(id))
            return (BadRequestBody("Invalid Id", "INVALID_ID"), null);

        if (!ModelState.IsValid)
        {
            return (BadRequest(ModelState), null);
        }

        try
        {
            _currentService.EnsurePatchable(changes);

            // looked up inside the scope: an entity outside the scope is a 404, as on GET
            var entity = await _currentService.GetByKey(id, fixCriteria: GetFixCriteria, cancellationToken: cancellationToken);
            if (entity == null)
            {
                return (NotFoundEntity(id), null);
            }

            changes.ApplyTo(entity, error =>
            {
                if (error.Operation.OperationType != OperationType.Test &&
                    error.Operation.OperationType != OperationType.Invalid)
                {
                    _currentService.HandleJsonOperator(error.Operation, error.AffectedObject);
                }
            });

            // the patch must not move the entity out of the scope it was found in; nothing is saved
            if (OutOfScope(entity))
            {
                return (NotFoundEntity(id), null);
            }

            var validationResult = _validateService != null ? await _validateService.Validate(entity, cancellationToken) : null;
            if (validationResult is { IsValid: false })
            {
                return (BadRequestBody("Invalid patch request", "INVALID_PATCH_REQUEST", validationResult), null);
            }

            // saved inside a transaction that the database scope check can veto (foreign keys and navigations count there)
            await _currentService.UpdateChanges(id, entity, GetFixCriteria, cancellationToken);

            return (null, entity);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to update {EntityName}", EntityName);
            return (Error(ex), null);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update {EntityName}", EntityName);
            return (Error(HttpStatusCode.InternalServerError,
                ApiErrorResponses.Failure($"Failed to update {EntityName}.", "UPDATE_ENTITY_FAILED", HttpContext)), null);
        }
    }

    private bool OutOfScope(TEntity entity)
    {
        var scope = GetFixCriteria;
        return scope != null && !scope.Compile()(entity);
    }

    private static bool IsDefaultKey(TKey id) => id == null || EqualityComparer<TKey>.Default.Equals(id, default!); // justified: a key of a generic type; default is the unset value the comparison wants

    /// <summary>
    /// Partially update Entity using patch document
    /// </summary>
    /// <param name="id">Entity identifier</param>
    /// <param name="changes">The patch document</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated Entity</returns>
    protected virtual async Task<IActionResult> PatchEntity([FromRoute] TKey id,
        [FromBody] JsonPatchDocument<TEntity> changes, CancellationToken cancellationToken = default)
    {
        var (response, entity) = await InternalPatchEntity(id, changes, cancellationToken);
        if (response != null)
            return response;
        if (_mapper == null)
            return Ok(entity);
        return Ok(_mapper.MapToViewModel(entity!)); // justified: the Ok/Mapper branch is only reached after the patch succeeded, so the entity is set
    }

    /// <summary>
    /// Partially update Entity using json document: only the properties present in the JSON change.
    /// </summary>
    /// <param name="id">Entity identifier</param>
    /// <param name="changes">Partial entity as JSON</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated Entity</returns>
    protected virtual async Task<IActionResult> PatchEntityByJson([FromRoute] TKey id, [FromBody] JsonObject changes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to patch {EntityName} : {Id}", EntityName, id);

        if (IsDefaultKey(id)) return BadRequestBody("Invalid Id", "INVALID_ID");

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            _currentService.EnsurePatchable(changes);

            // looked up inside the scope: an entity outside the scope is a 404, as on GET
            var entity = await _currentService.GetByKey(id, fixCriteria: GetFixCriteria, cancellationToken: cancellationToken);
            if (entity == null)
            {
                return NotFoundEntity(id);
            }

            _currentService.ApplyJson(changes, entity);

            // the patch must not move the entity out of the scope it was found in; nothing is saved
            if (OutOfScope(entity))
            {
                return NotFoundEntity(id);
            }

            var validationResult = _validateService != null ? await _validateService.Validate(entity, cancellationToken) : null;
            if (validationResult is { IsValid: false })
            {
                return BadRequestBody("Invalid patch request", "INVALID_PATCH_REQUEST", validationResult);
            }

            await _currentService.UpdateChanges(id, entity, GetFixCriteria, cancellationToken);

            return Ok(entity);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to update {EntityName} with id : {Id}", EntityName, id);
            return Error(ex);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update {EntityName} with id : {Id}", EntityName, id);
            return Error(HttpStatusCode.InternalServerError,
                ApiErrorResponses.Failure($"Failed to update {EntityName}.", "UPDATE_ENTITY_FAILED", HttpContext));
        }
    }

    /// <summary>
    /// Delete Entities (soft Delete)
    /// </summary>
    /// <param name="id">Entity identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual async Task<IActionResult> DeleteEntity([FromRoute] TKey id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("REST request to delete {EntityName} : {Id}", EntityName, id);

        // looked up inside the scope: an entity outside the scope is a 404, as on GET
        if (await _currentService.GetByKey(id, fixCriteria: GetFixCriteria, cancellationToken: cancellationToken) == null)
        {
            return NotFoundEntity(id);
        }

        try
        {
            if (!await _currentService.CanDelete(id, cancellationToken))
            {
                return Error(HttpStatusCode.PreconditionFailed,
                    ApiErrorResponses.Failure($"{EntityName} #{id} can't be deleted", $"{EntityName}_Cannot_Delete".ToUpperInvariant(), HttpContext));
            }

            await _currentService.Delete(id, cancellationToken);
            return Ok(new
            {
                Message = $"{EntityName} deleted successfully",
                Code = $"{EntityName}_Deleted".ToUpperInvariant()
            });
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to delete {EntityName}", EntityName);
            return Error(ex);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete {EntityName}", EntityName);
            return Error(HttpStatusCode.InternalServerError,
                ApiErrorResponses.Failure($"Failed to delete {EntityName}.", "DELETE_ENTITY_FAILED", HttpContext));
        }
    }

    /// <summary>
    /// Delete Entity (soft Delete)
    /// </summary>
    /// <param name="ids">Entity identifiers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual async Task<IActionResult> DeleteEntities([FromBody] IEnumerable<TKey> ids,
        CancellationToken cancellationToken = default)
    {
        var keys = ids.ToList();
        _logger.LogDebug("REST request to delete list of {EntityName}, {Count} ids", EntityName, keys.Count);

        try
        {
            if (GetFixCriteria != null)
            {
                // every id must be inside the scope (one query), otherwise the whole request is a 404
                var distinct = keys.Distinct().Count();
                if (await _currentService.CountByKeys(keys, GetFixCriteria, cancellationToken) != distinct)
                {
                    return NotFound(ApiErrorResponses.NotFound(EntityName, null, HttpContext));
                }
            }

            if (!await _currentService.CanDelete(keys, cancellationToken))
                return Error(HttpStatusCode.PreconditionFailed,
                    ApiErrorResponses.Failure($"{EntityName} can't be deleted", $"{EntityName}_Cannot_Delete".ToUpperInvariant(), HttpContext));

            await _currentService.Delete(keys, cancellationToken);
            return Ok(new { Message = $"{EntityName} deleted successfully", Code = $"{EntityName}_Deleted".ToUpperInvariant() });
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to delete list of {EntityName}", EntityName);
            return Error(ex);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete list of {EntityName}", EntityName);
            return Error(HttpStatusCode.InternalServerError,
                ApiErrorResponses.Failure($"Failed to delete {EntityName}.", "DELETE_ENTITY_FAILED", HttpContext));
        }
    }
}
