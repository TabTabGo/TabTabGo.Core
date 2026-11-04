using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using TabTabGo.Core.Api.Middlewares.Logging;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.WebApi.Middlewares;

namespace TabTabGo.Core.Api.Tests;

public class MiddlewareTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(HttpClient Client, FakeLogCollector Logs, WebApplication App)> StartAsync(
        Action<WebApplication> pipeline, Action<IServiceCollection>? services = null)
    {
        var logs = new FakeLogCollector();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FakeLoggerProvider(logs));
        services?.Invoke(builder.Services);
        var app = builder.Build();
        pipeline(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app.GetTestClient(), logs, app);
    }

    // =========================== ErrorHandlingMiddleware ===========================

    private static Task<(HttpClient Client, FakeLogCollector Logs, WebApplication App)> ErrorApp(Func<HttpContext, Task> endpoint, Action<ErrorHandlingMiddlewareOptions>? options = null) =>
        StartAsync(app =>
        {
            app.UseTabTabGoErrorHandling(options);
            ((IApplicationBuilder)app).Run(new RequestDelegate(endpoint));
        });

    [Fact]
    public async Task ErrorHandling_ApiException_KeepsStatusCodeNumberAndMessage()
    {
        var (client, _, app) = await ErrorApp(_ => throw new ApiException("nope", errorNumber: 42, statusCode: HttpStatusCode.Conflict, code: "CLASH"));

        var response = await client.GetAsync("/", Ct);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nope", body.GetProperty("message").GetString());
        Assert.Equal("CLASH", body.GetProperty("code").GetString());
        Assert.Equal(42, body.GetProperty("number").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ErrorHandling_ObsoleteTtgException_Mapped()
    {
#pragma warning disable CS0618
        var (client, _, app) = await ErrorApp(_ => throw new TTGException("old", null, Microsoft.Extensions.Logging.LogLevel.Error, 7, HttpStatusCode.NotFound, "OLD"));
#pragma warning restore CS0618

        var response = await client.GetAsync("/", Ct);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("OLD", body.GetProperty("code").GetString());
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ErrorHandling_UnexpectedException_Is500WithGenericMessageAndNoLeak()
    {
        // the exception object, its message and stack trace used to go to the client
        var (client, _, app) = await ErrorApp(_ => throw new InvalidOperationException("connection string Server=secret;Password=hunter2"));

        var response = await client.GetAsync("/", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("InvalidOperationException", text);
        Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("UNEXPECTED_ERROR", JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ErrorHandling_IncludeExceptionDetails_AddsDetailsOnlyWhenEnabled()
    {
        var (client, _, app) = await ErrorApp(_ => throw new InvalidOperationException("boom"), o => o.IncludeExceptionDetails = true);

        var body = JsonDocument.Parse(await (await client.GetAsync("/", Ct)).Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Contains("boom", body.GetProperty("details").GetString());
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ErrorHandling_LogsOnce_ErrorForServerAndWarningForClientErrors()
    {
        var (client, logs, app) = await ErrorApp(ctx => ctx.Request.Path == "/client"
            ? throw new ApiException("bad", statusCode: HttpStatusCode.BadRequest)
            : throw new InvalidOperationException("x"));

        await client.GetAsync("/client", Ct);
        await client.GetAsync("/server", Ct);

        var records = logs.GetSnapshot().Where(r => r.Category == typeof(ErrorHandlingMiddleware).FullName).ToList();
        Assert.Equal(2, records.Count);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, records[0].Level);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, records[1].Level);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ErrorHandling_ResponseAlreadyStarted_ExceptionIsRethrown()
    {
        var (client, logs, app) = await ErrorApp(async ctx =>
        {
            await ctx.Response.WriteAsync("partial", Ct);
            await ctx.Response.Body.FlushAsync(Ct);
            throw new InvalidOperationException("late");
        });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await client.GetAsync("/", HttpCompletionOption.ResponseHeadersRead, Ct);
            await response.Content.ReadAsStringAsync(Ct);
        });

        Assert.Contains(logs.GetSnapshot(), r => r.Message.Contains("response started"));
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ErrorHandling_ClientAborted_NoBodyNoErrorLog()
    {
        var (_, logs, app) = await ErrorApp(ctx => throw new OperationCanceledException(ctx.RequestAborted));
        var middleware = new ErrorHandlingMiddleware(_ => throw new OperationCanceledException(), new FakeLogger<ErrorHandlingMiddleware>(logs), Options.Create(new ErrorHandlingMiddlewareOptions()));
        var context = new DefaultHttpContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        context.RequestAborted = cts.Token;

        await middleware.Invoke(context);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ErrorHandling_NoException_PassesThrough()
    {
        var (client, _, app) = await ErrorApp(ctx => ctx.Response.WriteAsync("fine", Ct));

        Assert.Equal("fine", await (await client.GetAsync("/", Ct)).Content.ReadAsStringAsync(Ct));
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ErrorHandling_MapException_ReturnsStatusAndBody()
    {
        var middleware = new ErrorHandlingMiddleware(_ => Task.CompletedTask, new FakeLogger<ErrorHandlingMiddleware>(), Options.Create(new ErrorHandlingMiddlewareOptions()));
        var context = new DefaultHttpContext();

        var (status, body) = middleware.MapException(context, new ApiException("m", statusCode: HttpStatusCode.Forbidden, code: "C"));
        var (status2, body2) = middleware.MapException(context, new Exception("secret"));

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("C", body.Code);
        Assert.Equal(HttpStatusCode.InternalServerError, status2);
        Assert.DoesNotContain("secret", body2.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public void ErrorHandling_Ctor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ErrorHandlingMiddleware(null!, new FakeLogger<ErrorHandlingMiddleware>(), Options.Create(new ErrorHandlingMiddlewareOptions())));
        Assert.Throws<ArgumentNullException>(() => new ErrorHandlingMiddleware(_ => Task.CompletedTask, null!, Options.Create(new ErrorHandlingMiddlewareOptions())));
        Assert.Throws<ArgumentNullException>(() => new ErrorHandlingMiddleware(_ => Task.CompletedTask, new FakeLogger<ErrorHandlingMiddleware>(), null!));
        Assert.Throws<ArgumentNullException>(() => ErrorHandlingMiddlewareExtensions.UseTabTabGoErrorHandling(null!));
    }

    [Fact]
    public async Task ErrorHandling_UseMiddlewareWithoutExtension_UsesDiOptions()
    {
        var (client, _, app) = await StartAsync(a =>
        {
            a.UseMiddleware<ErrorHandlingMiddleware>();
            a.Run(_ => throw new InvalidOperationException("boom"));
        }, s => s.Configure<ErrorHandlingMiddlewareOptions>(o => o.IncludeExceptionDetails = true));

        var body = JsonDocument.Parse(await (await client.GetAsync("/", Ct)).Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.True(body.TryGetProperty("details", out _));
        await app.DisposeAsync();
    }

    // =========================== RequestResponseLoggingMiddleware ===========================

    private static Task<(HttpClient Client, FakeLogCollector Logs, WebApplication App)> LoggingApp(
        RequestResponseLoggingMiddlewareOptions options, Func<HttpContext, Task> endpoint) =>
        StartAsync(app =>
        {
            app.UseRequestResponseLogging(options);
            ((IApplicationBuilder)app).Run(new RequestDelegate(endpoint));
        });

    private static IReadOnlyList<FakeLogRecord> LoggingRecords(FakeLogCollector logs) =>
        logs.GetSnapshot().Where(r => r.Category == typeof(RequestResponseLoggingMiddleware).FullName).ToList();

    private static string ScopeValue(FakeLogRecord record, string name) =>
        record.Scopes.OfType<IEnumerable<KeyValuePair<string, object>>>().SelectMany(s => s).First(kv => kv.Key == name).Value.ToString()!;

    [Fact]
    public async Task Logging_RedactsCredentialHeaders()
    {
        var (client, logs, app) = await LoggingApp(new RequestResponseLoggingMiddlewareOptions(), ctx => ctx.Response.WriteAsync("ok", Ct));
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Authorization", "Bearer super-secret-token");
        request.Headers.Add("Cookie", "session=abc");
        request.Headers.Add("X-Api-Key", "key-123");
        request.Headers.Add("Accept-Language", "ar");

        await client.SendAsync(request, Ct);

        var requestRecord = LoggingRecords(logs).First();
        var headers = ScopeValue(requestRecord, "HeadersJson");
        Assert.DoesNotContain("super-secret-token", headers);
        Assert.DoesNotContain("abc", headers);
        Assert.DoesNotContain("key-123", headers);
        Assert.Contains("***", headers);
        Assert.Contains("\"ar\"", headers); // other headers stay readable
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Logging_ExtraRedactedHeaders_AreHonoured()
    {
        var options = new RequestResponseLoggingMiddlewareOptions();
        options.RedactedHeaders.Add("X-Tenant-Secret");
        var (client, logs, app) = await LoggingApp(options, _ => Task.CompletedTask);
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Tenant-Secret", "s3cret");

        await client.SendAsync(request, Ct);

        Assert.DoesNotContain("s3cret", ScopeValue(LoggingRecords(logs).First(), "HeadersJson"));
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Logging_BodiesAreLogged_AsArgumentsNotTemplates()
    {
        // a body such as "{0}" or "{Name}" used to be interpreted as the message template
        var (client, logs, app) = await LoggingApp(new RequestResponseLoggingMiddlewareOptions { RequestBody = true, ResponseBody = true },
            async ctx => await ctx.Response.WriteAsync("response {0} {Name}", Ct));

        var response = await client.PostAsync("/", new StringContent("request {0} {Name}", Encoding.UTF8, "text/plain"), Ct);

        Assert.Equal("response {0} {Name}", await response.Content.ReadAsStringAsync(Ct));
        var records = LoggingRecords(logs);
        Assert.Equal("request {0} {Name}", records[0].StructuredState!.First(kv => kv.Key == "RequestBody").Value);
        Assert.Equal("response {0} {Name}", records[1].StructuredState!.First(kv => kv.Key == "ResponseBody").Value);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Logging_LargeBodies_AreTruncated_ButTheClientGetsEverything()
    {
        var options = new RequestResponseLoggingMiddlewareOptions { RequestBody = true, ResponseBody = true, MaxBodyLength = 100 };
        var big = new string('x', 5000);
        var (client, logs, app) = await LoggingApp(options, async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var echoed = await reader.ReadToEndAsync(Ct); // the endpoint still sees the whole request body
            await ctx.Response.WriteAsync(echoed, Ct);
        });

        var response = await client.PostAsync("/", new StringContent(big), Ct);

        Assert.Equal(5000, (await response.Content.ReadAsStringAsync(Ct)).Length);
        var records = LoggingRecords(logs);
        var requestLogged = (string)records[0].StructuredState!.First(kv => kv.Key == "RequestBody").Value!;
        var responseLogged = (string)records[1].StructuredState!.First(kv => kv.Key == "ResponseBody").Value!;
        Assert.StartsWith(new string('x', 100), requestLogged);
        Assert.EndsWith("...[truncated]", requestLogged);
        Assert.EndsWith("...[truncated]", responseLogged);
        Assert.True(responseLogged.Length < 200);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Logging_BodiesOff_NothingIsRead()
    {
        var (client, logs, app) = await LoggingApp(new RequestResponseLoggingMiddlewareOptions(), ctx => ctx.Response.WriteAsync("hello", Ct));

        await client.PostAsync("/", new StringContent("payload"), Ct);

        Assert.All(LoggingRecords(logs).Take(2), r => Assert.Equal(string.Empty, r.StructuredState!.First(kv => kv.Key.EndsWith("Body")).Value));
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Logging_ChunkedRequestWithoutContentLength_IsStillLogged()
    {
        var (client, logs, app) = await LoggingApp(new RequestResponseLoggingMiddlewareOptions { RequestBody = true }, _ => Task.CompletedTask);
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("streamed body")));

        await client.PostAsync("/", content, Ct);

        Assert.Equal("streamed body", LoggingRecords(logs)[0].StructuredState!.First(kv => kv.Key == "RequestBody").Value);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Logging_ResponseScope_CarriesStatusAndSuccessFlag()
    {
        var (client, logs, app) = await LoggingApp(new RequestResponseLoggingMiddlewareOptions(), ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        });

        await client.GetAsync("/", Ct);

        var response = LoggingRecords(logs)[1];
        Assert.Equal("404", ScopeValue(response, "StatusCode"));
        Assert.Equal("0", ScopeValue(response, "IsSuccess"));
        Assert.Equal("Response", ScopeValue(response, "TraceType"));
        Assert.Equal("1", ScopeValue(LoggingRecords(logs)[0], "IsSuccess"));
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Logging_Exception_IsLoggedAndRethrownWithTheOriginalStackTrace()
    {
        var middleware = new RequestResponseLoggingMiddleware(_ => throw new InvalidOperationException("kaboom"), new FakeLoggerFactory(),
            new RequestResponseLoggingMiddlewareOptions());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context));

        // "throw ex" reset the stack trace to the middleware; "throw;" keeps the throw site
        Assert.Contains(nameof(Logging_Exception_IsLoggedAndRethrownWithTheOriginalStackTrace), ex.StackTrace);
    }

    [Fact]
    public async Task Logging_ExceptionFromPipeline_LoggedOnceAsError()
    {
        var logs = new FakeLogCollector();
        var middleware = new RequestResponseLoggingMiddleware(_ => throw new InvalidOperationException("kaboom"), new FakeLoggerFactory(logs),
            new RequestResponseLoggingMiddlewareOptions());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context));

        Assert.Single(LoggingRecords(logs), r => r.Level == Microsoft.Extensions.Logging.LogLevel.Error);
    }

    [Fact]
    public async Task Logging_ResponseStreamIsRestoredAfterTheRequest()
    {
        var original = new MemoryStream();
        var middleware = new RequestResponseLoggingMiddleware(ctx => ctx.Response.WriteAsync("x", Ct), new FakeLoggerFactory(),
            new RequestResponseLoggingMiddlewareOptions { ResponseBody = true });
        var context = new DefaultHttpContext();
        context.Response.Body = original;

        await middleware.Invoke(context);

        Assert.Same(original, context.Response.Body);
        Assert.Equal("x", Encoding.UTF8.GetString(original.ToArray()));
    }

    [Fact]
    public void Logging_Ctor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RequestResponseLoggingMiddleware(null!, new FakeLoggerFactory(), new RequestResponseLoggingMiddlewareOptions()));
        Assert.Throws<ArgumentNullException>(() => new RequestResponseLoggingMiddleware(_ => Task.CompletedTask, null!, new RequestResponseLoggingMiddlewareOptions()));
        Assert.Throws<ArgumentNullException>(() => new RequestResponseLoggingMiddleware(_ => Task.CompletedTask, new FakeLoggerFactory(), null!));
    }

    [Fact]
    public void Logging_Options_Defaults()
    {
        var options = new RequestResponseLoggingMiddlewareOptions();

        Assert.False(options.RequestBody);
        Assert.False(options.ResponseBody);
        Assert.Equal(32768, options.MaxBodyLength);
        Assert.Empty(options.RedactedHeaders);
        Assert.Contains("Authorization", RequestResponseLoggingMiddlewareOptions.DefaultRedactedHeaders);
        Assert.Equal("***", RequestResponseLoggingMiddleware.RedactedValue);
    }

    // =========================== CappedCaptureStream ===========================

    [Fact]
    public async Task CappedCaptureStream_PassesEverythingThrough_CapturesOnlyTheCap()
    {
        var inner = new MemoryStream();
        await using var capture = new CappedCaptureStream(inner, 5);

        capture.Write(new byte[] { (byte)'a', (byte)'b', (byte)'c' }, 0, 3);
        capture.Write(new ReadOnlySpan<byte>(new[] { (byte)'d', (byte)'e', (byte)'f', (byte)'g' }));
        await capture.WriteAsync(new byte[] { (byte)'h' }, 0, 1, Ct);
        await capture.WriteAsync(new ReadOnlyMemory<byte>(new[] { (byte)'i' }), Ct);
        capture.Flush();
        await capture.FlushAsync(Ct);

        Assert.Equal("abcdefghi", Encoding.UTF8.GetString(inner.ToArray()));
        Assert.Equal("abcde", capture.GetCapturedText());
        Assert.True(capture.Truncated);
    }

    [Fact]
    public async Task CappedCaptureStream_UnderTheCap_IsNotTruncated_AndIsWriteOnly()
    {
        await using var capture = new CappedCaptureStream(new MemoryStream(), 100);
        capture.Write(new byte[] { 65 }, 0, 1);

        Assert.False(capture.Truncated);
        Assert.False(capture.CanRead);
        Assert.False(capture.CanSeek);
        Assert.True(capture.CanWrite);
        Assert.Throws<NotSupportedException>(() => capture.Length);
        Assert.Throws<NotSupportedException>(() => capture.Position);
        Assert.Throws<NotSupportedException>(() => capture.Position = 1);
        Assert.Throws<NotSupportedException>(() => capture.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => capture.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => capture.SetLength(1));
    }

    private sealed class FakeLoggerFactory(FakeLogCollector? collector = null) : ILoggerFactory
    {
        private readonly FakeLogCollector _collector = collector ?? new FakeLogCollector();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new FakeLogger(_collector, categoryName);

        public void Dispose()
        {
        }
    }
}
