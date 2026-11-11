using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TabTabGo.Core.Api.Middlewares.Logging;

//credit: https://gist.github.com/elanderson/c50b2107de8ee2ed856353dfed9168a2

/// <summary>
/// Logs requests and responses (headers, optionally bodies). Credential headers are redacted, bodies are capped at
/// <see cref="RequestResponseLoggingMiddlewareOptions.MaxBodyLength"/> characters and bodies are passed to the logger
/// as arguments, never as the message template.
/// </summary>
public class RequestResponseLoggingMiddleware
{
    /// <summary>The text that replaces a redacted header value.</summary>
    public const string RedactedValue = "***";

    private readonly RequestDelegate _next;
    private readonly ILogger _actionLogger;
    private readonly RequestResponseLoggingMiddlewareOptions _loggingOptions;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next delegate</param>
    /// <param name="loggerFactory">Logger factory</param>
    /// <param name="loggingOptions">Options</param>
    public RequestResponseLoggingMiddleware(RequestDelegate next,
        ILoggerFactory loggerFactory,
        RequestResponseLoggingMiddlewareOptions loggingOptions)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(loggingOptions);
        _next = next;
        _actionLogger = loggerFactory.CreateLogger<RequestResponseLoggingMiddleware>();
        _loggingOptions = loggingOptions;
    }

    /// <summary>Logs the request, runs the pipeline, logs the response.</summary>
    /// <param name="context">The request</param>
    public async Task Invoke(HttpContext context)
    {
        var stopWatch = Stopwatch.StartNew();

        var requestBodyText = _loggingOptions.RequestBody ? await GetRequestBody(context.Request) : string.Empty;

        using (_actionLogger.BeginScope(new[] {
            new KeyValuePair<string, object>("TraceType", "Request"),
            new KeyValuePair<string, object>("IsSuccess", 1),   //consider requests as always a success, the response will matter
            new KeyValuePair<string, object>("HeadersJson", GetHeaderJson(context.Request.Headers) ?? string.Empty)
        }))
        {
            _actionLogger.LogInformation("{RequestBody}", requestBodyText);
        }

        var originalBodyStream = context.Response.Body;
        CappedCaptureStream? capture = null;
        if (_loggingOptions.ResponseBody)
        {
            capture = new CappedCaptureStream(originalBodyStream, _loggingOptions.MaxBodyLength * 4);
            context.Response.Body = capture;
        }

        Exception? failure = null;
        try
        {
            await _next(context);
        }
        catch (Exception x)
        {
            failure = x;
            throw;
        }
        finally
        {
            stopWatch.Stop();
            context.Response.Body = originalBodyStream;
            var responseBodyText = capture != null ? Truncate(capture.GetCapturedText(), capture.Truncated) : string.Empty;
            capture?.Dispose();

            var isSuccess = context.Response.StatusCode > 399 || failure != null ? 0 : 1; //codes above 399 are errors
            using (_actionLogger.BeginScope(new[] {
                new KeyValuePair<string, object>("TraceType", "Response"),
                new KeyValuePair<string, object>("TraceIdentifier", context.TraceIdentifier),
                new KeyValuePair<string, object>("StatusCode", context.Response.StatusCode),
                new KeyValuePair<string, object>("HeadersJson", GetHeaderJson(context.Response.Headers) ?? string.Empty),
                new KeyValuePair<string, object>("IsSuccess", isSuccess),
                new KeyValuePair<string, object>("ProcessTime", stopWatch.Elapsed.TotalMilliseconds.ToString(CultureInfo.InvariantCulture))
            }))
            {
                if (failure != null)
                {
                    _actionLogger.LogError(failure, "{ResponseBody}", responseBodyText);
                }
                else
                {
                    _actionLogger.LogInformation("{ResponseBody}", responseBodyText);
                }
            }
        }
    }

    private string Truncate(string text, bool alreadyTruncated)
    {
        var max = _loggingOptions.MaxBodyLength;
        if (text.Length > max)
        {
            return text[..max] + "...[truncated]";
        }

        return alreadyTruncated ? text + "...[truncated]" : text;
    }

    private async Task<string> GetRequestBody(HttpRequest request)
    {
        request.EnableBuffering();
        if (!request.Body.CanSeek)
        {
            return string.Empty;
        }

        // read at most MaxBodyLength characters (plus one to know whether there is more), never the whole body
        var max = _loggingOptions.MaxBodyLength;
        var buffer = new char[max + 1];
        int read;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        }

        request.Body.Seek(0, SeekOrigin.Begin);
        return read > max ? new string(buffer, 0, max) + "...[truncated]" : new string(buffer, 0, read);
    }

    private string? GetHeaderJson(IHeaderDictionary? headers)
    {
        if (headers == null || headers.Count == 0) return null;

        var redacted = new HashSet<string>(RequestResponseLoggingMiddlewareOptions.DefaultRedactedHeaders, StringComparer.OrdinalIgnoreCase);
        redacted.UnionWith(_loggingOptions.RedactedHeaders);

        var jHeader = new JsonObject();
        foreach (var (name, value) in headers)
        {
            jHeader[name] = redacted.Contains(name) ? RedactedValue : value.ToString();
        }

        return JsonSerializer.Serialize(jHeader, SerializerEngine.JsonSerializationSettings);
    }
}

/// <summary>
/// Registration of <see cref="RequestResponseLoggingMiddleware"/>.
/// </summary>
public static class RequestResponseLoggingMiddlewareExtensions
{
    /// <summary>Adds the request and response logging middleware.</summary>
    /// <param name="builder">The application builder</param>
    /// <param name="loggingOptions">Options</param>
    public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder builder, RequestResponseLoggingMiddlewareOptions loggingOptions)
    {
        return builder.UseMiddleware<RequestResponseLoggingMiddleware>(loggingOptions);
    }
}

/// <summary>
/// Options of <see cref="RequestResponseLoggingMiddleware"/>.
/// </summary>
public class RequestResponseLoggingMiddlewareOptions
{
    /// <summary>Header names whose values are always replaced by <c>***</c> in the log.</summary>
    public static readonly string[] DefaultRedactedHeaders =
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "X-Auth-Token", "X-Csrf-Token"
    };

    /// <summary>Log the request body.</summary>
    public bool RequestBody { get; set; }

    /// <summary>Log the response body.</summary>
    public bool ResponseBody { get; set; }

    /// <summary>Largest body, in characters, that is logged; the rest is cut.</summary>
    public int MaxBodyLength { get; set; } = 32768;

    /// <summary>Extra header names to redact, on top of <see cref="DefaultRedactedHeaders"/>.</summary>
    public ICollection<string> RedactedHeaders { get; set; } = new List<string>();
}
