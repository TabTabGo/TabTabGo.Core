using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TabTabGo.Core.Api.Controllers;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services;
using TabTabGo.Core.ViewModels;
using TabTabGo.Core.Services.ViewModels;
using TabTabGo.Core.WebApi.Middlewares;

namespace TabTabGo.Core.Test.Api;

/// <summary>
/// R10 / D-7: exceptions must never reach API clients.
/// </summary>
public class ErrorLeakTests
{
    private const string LeakMarker = "internal-host-db-prod-LEAKMARKER";

    private static void AssertNoLeak(string body)
    {
        Assert.DoesNotContain(LeakMarker, body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("System.", body);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", body);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int Status, string Body)> RunMiddleware(Exception toThrow)
    {
        var middleware = new ErrorHandlingMiddleware(_ => throw toThrow, NullLogger<ErrorHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    [Fact]
    public async Task Middleware_UnhandledException_ReturnsGenericBodyWithoutLeak()
    {
        var thrown = new InvalidOperationException(LeakMarker, new Exception("inner " + LeakMarker));

        var (status, body) = await RunMiddleware(thrown);

        Assert.Equal(500, status);
        AssertNoLeak(body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(new[] { "code", "message", "number" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Middleware_ApiException_KeepsClientMessageStatusAndCode()
    {
        var thrown = new ApiException("Order is locked", new InvalidOperationException(LeakMarker),
            errorNumber: 4001, statusCode: HttpStatusCode.Conflict, code: "ORDER_LOCKED");

        var (status, body) = await RunMiddleware(thrown);

        Assert.Equal(409, status);
        AssertNoLeak(body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Order is locked", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("ORDER_LOCKED", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(4001, json.RootElement.GetProperty("number").GetInt32());
    }

    [Fact]
    public void ErrorResponse_FromApiException_NeverCarriesException()
    {
        var api = new ApiException("msg", new InvalidOperationException(LeakMarker), code: "C");

        var response = new ErrorResponse(api);
        var withParam = new ErrorResponse("m", "c", 1, new InvalidOperationException(LeakMarker));

#pragma warning disable CS0618
        Assert.Null(response.Exception);
        Assert.Null(withParam.Exception);
#pragma warning restore CS0618
        AssertNoLeak(JsonSerializer.Serialize(response));
        AssertNoLeak(JsonSerializer.Serialize(withParam));
        AssertNoLeak(Newtonsoft.Json.JsonConvert.SerializeObject(withParam));
    }

    [Fact]
    public void ResponseViewModels_ExceptionProperty_IsNeverSerialised()
    {
#pragma warning disable CS0618
        var error = new ErrorResponseViewModel { Message = "m", Exception = new InvalidOperationException(LeakMarker) };
        var generic = new ResponseViewModel { Message = "m", Exception = new InvalidOperationException(LeakMarker) };
#pragma warning restore CS0618

        foreach (var model in new object[] { error, generic })
        {
            AssertNoLeak(JsonSerializer.Serialize(model, model.GetType()));
            AssertNoLeak(Newtonsoft.Json.JsonConvert.SerializeObject(model));
        }
    }

    [Fact]
    public async Task EntityController_DeleteFailure_ReturnsGenericBodyWithoutLeak()
    {
        var controller = new TestController(ServiceStub.Create(new InvalidOperationException(LeakMarker)));

        var result = await controller.Delete(1);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        var body = JsonSerializer.Serialize(objectResult.Value);
        AssertNoLeak(body);
        Assert.Contains("DELETE_ENTITY_FAILED", body);
        Assert.Contains("An unexpected error occurred.", body);
    }

    [Fact]
    public async Task EntityController_DeleteApiException_KeepsClientMessage()
    {
        var controller = new TestController(ServiceStub.Create(
            new ApiException("Cannot delete: in use", statusCode: HttpStatusCode.Conflict)));

        var result = await controller.Delete(1);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, objectResult.StatusCode);
        Assert.Contains("Cannot delete: in use", JsonSerializer.Serialize(objectResult.Value));
    }

    [Fact]
    public async Task EntityController_CreateFailure_ReturnsGenericBodyWithoutLeak()
    {
        var controller = new TestController(ServiceStub.Create(new InvalidOperationException(LeakMarker)));

        var result = await controller.Create(new CreateRequest());

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        AssertNoLeak(JsonSerializer.Serialize(objectResult.Value));
    }

    [Fact]
    public async Task EntityController_PatchFailure_ReturnsGenericBodyWithoutLeak()
    {
        // no validator: v8 InternalPatchEntity answers 400 when a validator says the patch IS valid (inverted check, not part of this fix)
        var controller = new TestController(ServiceStub.Create(new InvalidOperationException(LeakMarker)), withValidator: false);

        var result = await controller.Patch(1);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        AssertNoLeak(JsonSerializer.Serialize(objectResult.Value));
    }

    [Fact]
    public async Task EntityController_PatchByJsonFailure_ReturnsGenericBodyWithoutLeak()
    {
        var controller = new TestController(ServiceStub.Create(new InvalidOperationException(LeakMarker)));

        var result = await controller.PatchJson(1);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        AssertNoLeak(JsonSerializer.Serialize(objectResult.Value));
    }

    [Fact]
    public async Task EntityController_DeleteListFailure_ReturnsGenericBodyWithoutLeak()
    {
        var controller = new TestController(ServiceStub.Create(new InvalidOperationException(LeakMarker)));

        var result = await controller.DeleteMany(new[] { 1, 2 });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        AssertNoLeak(JsonSerializer.Serialize(objectResult.Value));
    }

    [Fact]
    public async Task EntityController_ApiExceptionWithDefaultMessage_SendsTheGenericMessageNotTheTypeName()
    {
        var controller = new TestController(ServiceStub.Create(new ApiException()));

        var result = await controller.Delete(1);

        var body = JsonSerializer.Serialize(Assert.IsType<ObjectResult>(result).Value);
        Assert.DoesNotContain("ApiException", body);
        Assert.Contains("An unexpected error occurred.", body);
    }

    [Fact]
    public void Api_NoAnonymousResponseTypeCarriesAnExceptionProperty()
    {
        // guards every Results.Json / StatusCode site of MinimalBaseApi and the controllers against a regression to `exception = e`
        var anonymous = typeof(EntityControllerBase<,,,,>).Assembly.GetTypes()
            .Where(t => t.Name.Contains("AnonymousType") && t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() != null)
            .ToList();

        Assert.NotEmpty(anonymous);
        foreach (var type in anonymous)
        {
            foreach (var property in type.GetProperties())
            {
                Assert.False(typeof(Exception).IsAssignableFrom(property.PropertyType), $"{type.Name}.{property.Name}");
                Assert.False(property.Name.Equals("exception", StringComparison.OrdinalIgnoreCase), $"{type.Name}.{property.Name}");
            }
        }
    }

    [Fact]
    public async Task Middleware_ObsoleteTtgException_IsMappedLikeApiException()
    {
#pragma warning disable CS0618
        var thrown = new TTGException("Old rule broken", new InvalidOperationException(LeakMarker),
            Microsoft.Extensions.Logging.LogLevel.Warning, 7, HttpStatusCode.NotFound, "OLD_CODE");
#pragma warning restore CS0618

        var (status, body) = await RunMiddleware(thrown);

        Assert.Equal(404, status);
        AssertNoLeak(body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Old rule broken", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("OLD_CODE", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(7, json.RootElement.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Middleware_ApiExceptionWithDefaultMessage_SendsTheGenericMessageNotTheTypeName()
    {
        var (status, body) = await RunMiddleware(new ApiException());

        Assert.Equal(500, status);
        Assert.DoesNotContain("ApiException", body);
        Assert.Contains("An unexpected error occurred.", body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, Microsoft.Extensions.Logging.LogLevel.Error, Microsoft.Extensions.Logging.LogLevel.Warning)]
    [InlineData(HttpStatusCode.BadRequest, Microsoft.Extensions.Logging.LogLevel.Information, Microsoft.Extensions.Logging.LogLevel.Information)]
    [InlineData(HttpStatusCode.InternalServerError, Microsoft.Extensions.Logging.LogLevel.Information, Microsoft.Extensions.Logging.LogLevel.Error)]
    public async Task Middleware_LogsAtTheLevelOfTheApiException_WarningAtMostBelow500_ErrorFor500(
        HttpStatusCode status, Microsoft.Extensions.Logging.LogLevel exceptionLevel, Microsoft.Extensions.Logging.LogLevel expected)
    {
        var logger = new RecordingLogger();
        var middleware = new ErrorHandlingMiddleware(_ => throw new ApiException("x", null, exceptionLevel, 1, status, "C"), logger);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);

        Assert.Equal(expected, Assert.Single(logger.Levels));
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<ErrorHandlingMiddleware>
    {
        public List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    public class TestEntity : IEntity
    {
        public string CreatedBy { get; set; } = "";
        public DateTimeOffset CreatedDate { get; set; }
        public bool IsEnabled { get; set; }
        public IDictionary<string, object> ExtraProperties { get; set; } = new Dictionary<string, object>();
        public string UpdatedBy { get; set; } = "";
        public DateTimeOffset UpdatedDate { get; set; }
    }

    public class TestController(IBaseService<TestEntity, int> service, bool withValidator = true)
        : EntityControllerBase<TestEntity, int, TestEntity, CreateRequest, TestEntity>(
            NullLogger.Instance, service, null, withValidator ? new AlwaysValid() : null)
    {
        public Task<IActionResult> Delete(int id) => DeleteEntity(id);
        public Task<IActionResult> Create(CreateRequest request) => CreateEntity(request);
        public Task<IActionResult> Patch(int id) =>
            PatchEntity(id, new Microsoft.AspNetCore.JsonPatch.JsonPatchDocument<TestEntity>());
        public Task<IActionResult> PatchJson(int id) => PatchEntityByJson(id, new System.Text.Json.Nodes.JsonObject());
        public Task<IActionResult> DeleteMany(IEnumerable<int> ids) => DeleteEntities(ids);
    }

    public class CreateRequest
    {
    }

    public class AlwaysValid : IValidationService<TestEntity, CreateRequest>
    {
        public Task<ValidationResult> Validate(TestEntity entity, CancellationToken cancellationToken) =>
            Task.FromResult(new ValidationResult { IsValid = true });

        public Task<ValidationResult> Validate(CreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ValidationResult { IsValid = true });
    }

    /// <summary>
    /// Stub service: Exists/CanDelete succeed, every mutating call throws the given exception.
    /// </summary>
    public class ServiceStub : DispatchProxy
    {
        private Exception _toThrow = null!;

        public static IBaseService<TestEntity, int> Create(Exception toThrow)
        {
            var proxy = DispatchProxy.Create<IBaseService<TestEntity, int>, ServiceStub>();
            ((ServiceStub)(object)proxy)._toThrow = toThrow;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name is "Exists" or "CanDelete")
            {
                return Task.FromResult(true);
            }

            if (name == "GetByKey")
            {
                return Task.FromResult<TestEntity?>(new TestEntity());
            }

            if (name is "Delete" or "Create" or "UpdateChanges" or "Update")
            {
                var inner = targetMethod.ReturnType.GetGenericArguments()[0];
                var fromException = typeof(Task).GetMethods()
                    .Single(m => m.Name == nameof(Task.FromException) && m.IsGenericMethodDefinition);
                return fromException.MakeGenericMethod(inner).Invoke(null, new object[] { _toThrow });
            }

            throw new NotSupportedException(name);
        }
    }
}
