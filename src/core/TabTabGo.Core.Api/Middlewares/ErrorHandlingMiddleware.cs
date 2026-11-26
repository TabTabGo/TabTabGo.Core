using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TabTabGo.Core.Exceptions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TabTabGo.Core.WebApi.Middlewares
{
    /// <summary>
    /// Middleware to handle webApi error and return a json instead of html page. 
    /// <see cref="https://stackoverflow.com/questions/38630076/asp-net-core-web-api-exception-handling"/>
    /// </summary>
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate next;
        private readonly ILogger logger;

        /// <summary>
        /// Creates the middleware without a logger (unhandled exceptions are then not logged).
        /// </summary>
        public ErrorHandlingMiddleware(RequestDelegate next) : this(next, NullLogger<ErrorHandlingMiddleware>.Instance)
        {
        }

        /// <summary>
        /// Creates the middleware; the full exception is logged server-side.
        /// </summary>
        [ActivatorUtilitiesConstructor]
        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            this.next = next;
            this.logger = logger;
        }

        public async Task Invoke(HttpContext context /* other scoped dependencies */)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Only ApiException (and the obsolete TTGException) messages are written for clients; anything else gets the generic
            // body. Never send exception.Message, stack, type name or the exception object itself.
            var httpCode = HttpStatusCode.InternalServerError;
            var level = LogLevel.Error;
            ClientError body;
            if (exception is ApiException apiException)
            {
                httpCode = apiException.HttpStatusCode;
                level = apiException.Level;
                body = new ClientError(apiException.ErrorNumber, apiException.ErrorCode, ClientError.MessageOf(apiException));
            }
#pragma warning disable CS0618 // TTGException is obsolete but still thrown by older consumer code
            else if (exception is TTGException ttgException)
            {
                httpCode = ttgException.HttpStatusCode == 0 ? HttpStatusCode.InternalServerError : ttgException.HttpStatusCode;
                level = ttgException.Level;
                body = new ClientError(ttgException.ErrorNumber, ttgException.ErrorCode, ClientError.MessageOf(ttgException));
            }
#pragma warning restore CS0618
            else
            {
                body = ClientError.Unexpected();
            }

            // a server error is always an Error; a client error is not one: at most a Warning, never louder than the exception asks for
            if ((int)httpCode >= 500)
            {
                level = LogLevel.Error;
            }
            else if (level is > LogLevel.Warning and < LogLevel.None)
            {
                level = LogLevel.Warning;
            }

            logger.Log(level, exception, "Request {Method} {Path} failed with {StatusCode}",
                context.Request.Method, context.Request.Path, (int)httpCode);

            var result = JsonConvert.SerializeObject(new
            {
                number = body.Number, code = body.Code, message = body.Message
            });
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)httpCode;
            return context.Response.WriteAsync(result);
        }
    }
}
