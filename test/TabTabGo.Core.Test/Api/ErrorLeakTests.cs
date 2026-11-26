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

    public class TestEntity : IEntity
    {
        public string CreatedBy { get; set; } = "";
        public DateTimeOffset CreatedDate { get; set; }
        public bool IsEnabled { get; set; }
        public IDictionary<string, object> ExtraProperties { get; set; } = new Dictionary<string, object>();
        public string UpdatedBy { get; set; } = "";
        public DateTimeOffset UpdatedDate { get; set; }
    }

    public class TestController(IBaseService<TestEntity, int> service)
        : EntityControllerBase<TestEntity, int, TestEntity, TestEntity, TestEntity>(
            NullLogger.Instance, service, null, null)
    {
        public Task<IActionResult> Delete(int id) => DeleteEntity(id);
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
