using System.Linq.Expressions;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Extensions;
using TabTabGo.Core.Services;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Api.Controllers;

public class EntityRouterOptions<TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest>
    where TEntity : class, IEntity
    where TViewModel : class
    where TCreateRequest : class
{
    public Expression<Func<TEntity, bool>>? SearchFixCriteria { get; set; } = null;
    public Expression<Func<TEntity, bool>>? GetFixCriteria { get; set; } = null;
    public Expression<Func<TEntity, bool>>? ExportFixCriteria { get; set; } = null;

    public string? ControllerPrefix { get; set; } = null;
    public string? Version { get; set; } = null;

    public string GetRoutePrefix()
    {
        var routeBuilder = new StringBuilder();
        if (!string.IsNullOrEmpty(ControllerPrefix))
        {
            routeBuilder.Append(ControllerPrefix + "/");
        }

        if (!string.IsNullOrEmpty(Version))
        {
            routeBuilder.Append(Version + "/");
        }

        return routeBuilder.ToString();
    }
}

public static class SetupEntityRouters
{
    public static WebApplication MapEntityRoutes<TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest>(
        this WebApplication app,
        string controllerName,
        Action<EntityRouterOptions<TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest>>? setEntityRouteOptions =
            null)
        where TEntity : class, IEntity
        where TViewModel : class
        where TCreateRequest : class
        where TUpdateRequest : class
    {
        if (string.IsNullOrEmpty(controllerName))
        {
            throw new InvalidOperationException("Controller name is required");
        }

        // Get services
        var currentService = app.Services.GetService<IBaseService<TEntity, TKey>>();
        if (currentService == null)
        {
            throw new InvalidOperationException($"No service found for {nameof(IBaseService<TEntity, TKey>)}");
        }

        var logger = app.Services.GetRequiredService<ILogger<TEntity>>();
        if (logger == null)
        {
            throw new InvalidOperationException($"No logger found for {nameof(TEntity)}");
        }

        var mapper = app.Services.GetService<IMapper<TEntity, TViewModel>>();
        var validateService =
            app.Services.GetService<IValidationService<TEntity, TCreateRequest>>();
        var options = new EntityRouterOptions<TEntity, TKey, TViewModel, TCreateRequest, TUpdateRequest>();
        setEntityRouteOptions?.Invoke(options);

        var entityName = typeof(TEntity).Name;

        #region Map Search route to get list of entities

        app.MapGet($"/{controllerName}", [Authorize] async (ODataQueryOptions<TEntity> query,
            CancellationToken cancellationToken = default) =>
        {
            logger.LogTrace("REST request to search {EntityName} on query {@Query}", nameof(TEntity), @query);
            var entitiesResult = await currentService.GetPageList(query, cancellationToken: cancellationToken);
            if (mapper == null) return Results.Ok(entitiesResult);
            var result = mapper.MapPaging(entitiesResult);
            return Results.Ok(result);
        });

        #endregion

        #region Map get route to get an entity

        app.MapGet($"{controllerName}/{{id}}", [Authorize] async (TKey id, DateTimeOffset? lastUpdatedDate = null,
            string? expand = null,
            CancellationToken cancellationToken = default) =>
        {
            logger.LogTrace("Getting {ControllerName} with id {Id}", entityName, id);
            try
            {
                var entity = await currentService.GetByKey(id, lastUpdatedDate,
                    expand?.Split(','),
                    fixCriteria: options.GetFixCriteria,
                    cancellationToken: cancellationToken);
                if (entity == null)
                {
                    return Results.NotFound(new
                    {
                        Message = $"{entityName} not found",
                        Code = $"{entityName}_Not_Found".ToUpper()
                    });
                }

                var entityViewModel = mapper?.MapToViewModel(entity);
                return Results.Ok(entityViewModel != null ? entityViewModel : entity);
            }
            catch (ApiException ex)
            {
                return Results.Json(new { ex.Message, Code = ex.ErrorCode },
                    SerializerEngine.JsonSerializationSettings, statusCode: (int)ex.HttpStatusCode);
            }
        });

        #endregion

        #region Export Entities  into different formats

        app.MapPost($"{controllerName}/export/{{format}}", [Authorize] async (ExportType format,
            ODataQueryOptions<TEntity> query,
            [FromBody] ExportConfiguration? export = null,
            CancellationToken cancellationToken = default) =>
        {
            logger.LogTrace("REST request to export ${EntityName} to type {ExportType}", entityName, format);
            export ??= new ExportConfiguration();
            if (string.IsNullOrEmpty(export.FileName))
            {
                var prefix = entityName;
                // add- if name have Upper case letter
                export.FileName = $"{prefix}_{DateTime.UtcNow:yyyyMMdd}.{format.FileExtenstion()}";
            }

            try
            {
                export.FileType = format;
                var stream = await currentService.ExportFile<object>(export, query,
                    fixCriteria: options.ExportFixCriteria,
                    cancellationToken: cancellationToken);

                return Results.Stream(stream, "application/octet-stream", export.FileName);
            }
            catch (ApiException ex)
            {
                return Results.Json(new { ex.Message, Code = ex.ErrorCode },
                    SerializerEngine.JsonSerializationSettings, statusCode: (int)ex.HttpStatusCode);
            }
        });

        #endregion

        #region Map post route to create a new entity

        app.MapPost($"/{controllerName}", [Authorize]
            async (TCreateRequest requestedEntity, CancellationToken cancellationToken = default) =>
            {
                logger.LogTrace("Creating a new {EntityName} with request {@Request}", entityName, requestedEntity);

                var validationResult = validateService != null
                    ? await validateService.Validate(requestedEntity, cancellationToken)
                    : null;

                if (validationResult is { IsValid: false })
                {
                    logger.LogInformation(
                        "Validation failed for {EntityName} with errors : {@ValidationResult} for request {@RequestedEntity}",
                        entityName, validationResult, requestedEntity);
                    return Results.BadRequest(new
                    {
                        Message = $"Validation failed for create {controllerName}",
                        Code = $"Create_Validation_Failed_{controllerName}".ToUpper(),
                        validationResult.Errors
                    });
                }

                try
                {
                    logger.LogInformation("Validation passed for {EntityName}", entityName);
                    var entity = mapper != null ? mapper.MapFromRequest(requestedEntity) : requestedEntity as TEntity;
                    var createdEntity = await currentService.Create(entity, cancellationToken);
                    var id = currentService.GetKey(createdEntity);
                    logger.LogInformation("Created {EntityName} with id {Id}", entityName, id);
                    var entityViewModel = mapper?.MapToViewModel(createdEntity);

                    return Results.Created($"/{controllerName}/{id}",
                        entityViewModel != null ? entityViewModel : createdEntity);
                }
                catch (ApiException ex)
                {
                    return Results.Json(new { ex.Message, Code = ex.ErrorCode },
                        SerializerEngine.JsonSerializationSettings, statusCode: (int)ex.HttpStatusCode);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to create {EntityName} : {@EntityRequest}", entityName,
                        @requestedEntity);
                    return Results.Json(
                        new
                        {
                            Message = $"Failed to create {entityName}.", Code = $"Create_{entityName}_Failed".ToUpper(),
                            exception = e
                        }, statusCode: (int)HttpStatusCode.InternalServerError);
                }
            });

        #endregion

        #region Map PATCH route for update entity

        app.MapPatch($"{controllerName}/{{id}}", [Authorize] async (TKey id,
            JsonPatchDocument<TEntity> changes,
            CancellationToken cancellationToken = default) =>
        {
            logger.LogTrace("REST request to patch ${EntityName} : {@Changes}", entityName, @changes);

            if (id == null || id.Equals(0))
                return Results.BadRequest(new
                {
                    Message = "Invalid Id", Errors = new Dictionary<string, string>()
                    {
                        { "Id", "Invalid Id" }
                    }
                });

            var entity = await currentService.GetByKey(id, cancellationToken: cancellationToken);
            if (entity == null)
            {
                return Results.NotFound(new
                {
                    Message = $"{entityName} not found for passed {id}", Code = $"{entityName}_Not_Found".ToUpper()
                });
            }

            changes.ApplyTo(entity, error =>
            {
                if (error.Operation.OperationType != OperationType.Test &&
                    error.Operation.OperationType != OperationType.Invalid)
                {
                    currentService.HandleJsonOperator(error.Operation, error.AffectedObject);
                }
            });

            var validationResult = validateService != null
                ? await validateService.Validate(entity, cancellationToken)
                : null;
            if (validationResult is { IsValid: true })
            {
                return Results.BadRequest(new
                {
                    Message = "Invalid patch request",
                    Code = "Invalid_Patch_Request".ToUpper(),
                     validationResult.Errors,
                });
            }

            try
            {
                await currentService.UpdateChanges(id, entity, cancellationToken);
                var entityViewModel = mapper?.MapToViewModel(entity);
                return Results.Ok(entityViewModel);
            }
            catch (ApiException ex)
            {
                logger.LogError(ex, "Failed to update {EntityName}, changes {@Changes}", entityName, changes);
                return Results.Json(new {  ex.Message, Code = ex.ErrorCode, },
                    statusCode: (int)ex.HttpStatusCode);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update {EntityName}, changes {@Changes}", entityName, changes);
                return
                    Results.Json(new {  e.Message, Code = "Update_Entity_Failed".ToUpper(), exception = e },
                        statusCode: (int)HttpStatusCode.InternalServerError);
            }
        });

        #endregion

        #region Map Delete route for delete entity

        app.MapDelete($"{controllerName}/{{id}}", [Authorize]
            async (TKey id, CancellationToken cancellationToken = default) =>
            {
                logger.LogDebug("REST request to delete ${EntityName} : {Id}", entityName, id);

                if (!await currentService.Exists(id, cancellationToken))
                {
                    return Results.NotFound(new
                    {
                        Message = $"{entityName} not found for passed {id}",
                        Code = $"{entityName}_Not_Found".ToUpper()
                    });
                }

                if (!await currentService.CanDelete(id, cancellationToken))
                {
                    return Results.Json(
                        new
                        {
                            Message = $"{entityName} #{id} can't be deleted",
                            Code = $"{entityName}_Cannot_Delete".ToUpper()
                        }, statusCode: (int)HttpStatusCode.PreconditionFailed);
                }

                try
                {
                     await currentService.Delete(id, cancellationToken);
                    return Results.Ok(new
                    {
                        Message = $"{entityName} deleted successfully",
                        Code = $"{entityName}_Deleted".ToUpper()
                    });
                }
                catch (ApiException ex)
                {
                    logger.LogError(ex, "Failed to delete {EntityName} with Id {Id}", entityName, id);
                    return Results.Json(new {  ex.Message, Code = ex.ErrorCode, },
                        statusCode: (int)ex.HttpStatusCode);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to delete {EntityName} with Id {Id}", entityName, id);
                    return Results.Json(
                        new { e.Message, Code = "Delete_Entity_Failed".ToUpper(), exception = e },
                        statusCode: (int)HttpStatusCode.InternalServerError);
                }
            });

        #endregion

        return app;
    }
}