using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TabTabGo.Core.Api.Errors;
using TabTabGo.Core.Exceptions;

namespace TabTabGo.Core.WebApi.Middlewares;

/// <summary>
/// Options of <see cref="ErrorHandlingMiddleware"/>.
/// </summary>
public class ErrorHandlingMiddlewareOptions
{
    /// <summary>
    /// When true the response also carries the exception type and stack trace (<c>details</c>). Leave false except in
    /// Development: stack traces must not reach clients.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }
}

/// <summary>
/// Middleware to handle webApi error and return a json instead of an html page. <see cref="ApiException"/> (and the
/// obsolete <c>TTGException</c>) keep their status code, code and number; any other exception is a 500 with a generic
/// message. The body is <c>{ number, code, message, traceId }</c>.
/// <see href="https://stackoverflow.com/questions/38630076/asp-net-core-web-api-exception-handling"/>
/// </summary>
public class ErrorHandlingMiddleware
{
    private const string UnexpectedMessage = "An unexpected error occurred.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly ErrorHandlingMiddlewareOptions _options;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next delegate</param>
    /// <param name="logger">Logger</param>
    /// <param name="options">Options</param>
    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger,
        IOptions<ErrorHandlingMiddlewareOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>Runs the rest of the pipeline and turns an exception into the error body.</summary>
    /// <param name="context">The request</param>
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // the client went away; there is nobody to answer and nothing to report
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogError(ex, "Unhandled exception after the response started; the response cannot be replaced.");
                throw;
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>Maps an exception to the status code and the body it is answered with.</summary>
    /// <param name="context">The request</param>
    /// <param name="exception">The exception</param>
    internal (HttpStatusCode Status, ApiErrorBody Body) MapException(HttpContext context, Exception exception)
    {
        if (exception is ApiException apiException)
        {
            return (apiException.HttpStatusCode, ApiErrorResponses.From(apiException, context));
        }

#pragma warning disable CS0618 // TTGException is obsolete but still thrown by older consumer code
        if (exception is TTGException ttgException)
        {
            return (ttgException.HttpStatusCode, ApiErrorResponses.From(ttgException, context));
        }
#pragma warning restore CS0618

        return (HttpStatusCode.InternalServerError, ApiErrorResponses.Failure(UnexpectedMessage, "UNEXPECTED_ERROR", context));
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, body) = MapException(context, exception);

        // logged once, here
        _logger.Log(status >= HttpStatusCode.InternalServerError ? LogLevel.Error : LogLevel.Warning, exception,
            "Request {Method} {Path} failed with {StatusCode} ({TraceId})", context.Request.Method, context.Request.Path,
            (int)status, body.TraceId);

        if (_options.IncludeExceptionDetails)
        {
            body = new ApiErrorBody
            {
                Message = body.Message,
                Code = body.Code,
                Number = body.Number,
                TraceId = body.TraceId,
                Details = exception.ToString(),
            };
        }

        context.Response.Clear();
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsJsonAsync(body, SerializerEngine.JsonSerializationSettings, "application/json");
    }
}

/// <summary>
/// Registration of <see cref="ErrorHandlingMiddleware"/>.
/// </summary>
public static class ErrorHandlingMiddlewareExtensions
{
    /// <summary>Adds the error handling middleware.</summary>
    /// <param name="builder">The application builder</param>
    /// <param name="configure">Optional options, for example <c>o.IncludeExceptionDetails = env.IsDevelopment()</c></param>
    public static IApplicationBuilder UseTabTabGoErrorHandling(this IApplicationBuilder builder,
        Action<ErrorHandlingMiddlewareOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ErrorHandlingMiddlewareOptions();
        configure?.Invoke(options);
        return builder.UseMiddleware<ErrorHandlingMiddleware>(Options.Create(options));
    }
}
