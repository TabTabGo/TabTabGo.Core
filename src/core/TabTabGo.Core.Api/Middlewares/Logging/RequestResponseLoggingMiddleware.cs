using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TabTabGo.Core.Api.Middlewares.Logging
{
    //credit: https://gist.github.com/elanderson/c50b2107de8ee2ed856353dfed9168a2
    public class RequestResponseLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger _actionLogger;
        private readonly ILogger _logger;
        private readonly RequestResponseLoggingMiddlewareOptions loggingOptions;

        public RequestResponseLoggingMiddleware(RequestDelegate next,
                                                ILoggerFactory loggerFactory,
                                                RequestResponseLoggingMiddlewareOptions loggingOptions)
        {
            _next = next;
            _actionLogger = loggerFactory
                      .CreateLogger<RequestResponseLoggingMiddleware>();
            _logger = loggerFactory.CreateLogger("TabTabGo.Core.Web");
            this.loggingOptions = loggingOptions;
        }

        //TODO: use configuration to determin whether we should log full request/response body or not. its quite big in the most cases
        public async Task Invoke(HttpContext context)
        {

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var requestBodyText = loggingOptions.RequestBody ? await GetRequestBody(context.Request) : "";

            /*
            //Log<TState> Simple as explained here https://github.com/NLog/NLog.Extensions.Logging/wiki/NLog-properties-with-Microsoft-Extension-Logging
            TStatePropertiesMapperEventInfo eventInfo = new TStatePropertiesMapperEventInfo(requestBody)
                .AddProp("TraceType", "Request");

            _actionLogger.Log(LogLevel.Information,
                default(EventId),
                eventInfo,
                (Exception)null,
                TStatePropertiesMapperEventInfo.Formatter);
            */

            using (_actionLogger.BeginScope(new[] {
                new KeyValuePair<string, object>("TraceType", "Request"),
                new KeyValuePair<string, object>("IsSuccess", 1),   //consider requests as always a success, the response will matter
                new KeyValuePair<string, object>("HeadersJson", GetHeaderJson(context.Request.Headers))
                
            }))
            {
                _actionLogger.LogInformation(requestBodyText);
            }


            var originalBodyStream = context.Response.Body;

            using (var responseBody = new MemoryStream())
            {
                context.Response.Body = responseBody;

                Exception ex = null;
                try
                {
                    await _next(context);
                }
                catch (Exception x)
                {
                    //we only come here in prod when DeveloperExceptionPage is not used, to test this comment out this line in startup.cs -->  app.UseDeveloperExceptionPage();
                    ex = x;
                    _logger.LogError(x, $"{context.TraceIdentifier}");
                }
                finally
                {
                    stopWatch.Stop();
                    var responseBodyText = loggingOptions.ResponseBody ? await GetResponseBody(context.Response) : "";
                    var isSuccess = context.Response.StatusCode > 399 ? 0 : 1;  //code above 399 are errors, 4xx client errors, 5xx server errors
                    if (ex != null)
                        isSuccess = 0;

                    using (_actionLogger.BeginScope(new[] {
                        new KeyValuePair<string, object>("TraceType", "Response"),
                        new KeyValuePair<string, object>("TraceIdentifier", context.TraceIdentifier),
                        new KeyValuePair<string, object>("StatusCode", context.Response.StatusCode),
                        new KeyValuePair<string, object>("HeadersJson", GetHeaderJson(context.Response.Headers)),
                        new KeyValuePair<string, object>("IsSuccess", isSuccess),
                        new KeyValuePair<string, object>("ProcessTime", stopWatch.Elapsed.TotalMilliseconds.ToString())
                }))
                    {
                        if (ex != null)
                        {
                            _actionLogger.LogError(ex, responseBodyText);
                            throw ex;
                            //re-throw it, let the original exception take its natural route after logging.
                            //implement custom error response here, if needed
                        }
                        else
                            _actionLogger.LogInformation(responseBodyText);
                    }

                    if (context.Response.StatusCode != 204)
                    {
                        await responseBody.CopyToAsync(originalBodyStream);
                    }
                }
            }
        }

        private async Task<string> GetRequestBody(HttpRequest request)
        {
            request.EnableBuffering();
            var body = request.Body;

            var buffer = new byte[Convert.ToInt32(request.ContentLength)];
            await request.Body.ReadAsync(buffer, 0, buffer.Length);
            var bodyAsText = Encoding.UTF8.GetString(buffer);
            body.Seek(0, SeekOrigin.Begin);
            request.Body = body;

            return bodyAsText;
        }

        private async Task<string> GetResponseBody(HttpResponse response)
        {
            response.Body.Seek(0, SeekOrigin.Begin);
            var bodyAsText = await new StreamReader(response.Body).ReadToEndAsync();
            response.Body.Seek(0, SeekOrigin.Begin);

            return bodyAsText;
        }

        private string? GetHeaderJson(IHeaderDictionary? headers)
        {
            if (headers == null || headers.Count == 0) return null;

            var jHeader = new JsonObject();

            foreach (var kv in headers)
                jHeader[kv.Key] = kv.Value.ToString();

            return JsonSerializer.Serialize(jHeader, SerializerEngine.JsonSerializationSettings);
        }
    }

    public static class RequestResponseLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder builder, RequestResponseLoggingMiddlewareOptions loggingOptions)
        {
            return builder.UseMiddleware<RequestResponseLoggingMiddleware>(loggingOptions);
        }
    }

    public class RequestResponseLoggingMiddlewareOptions
    {
        public bool RequestBody { get; set; }
        public bool ResponseBody { get; set; }
    }
}
