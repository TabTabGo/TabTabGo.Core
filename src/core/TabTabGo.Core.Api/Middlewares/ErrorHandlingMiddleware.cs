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
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                context.Request.Method, context.Request.Path);

            // Only ApiException messages are written for clients; anything else gets the generic body.
            // Never send exception.Message, stack, type name or the exception object itself.
            var httpCode = HttpStatusCode.InternalServerError;
            ClientError body;
            if (exception is ApiException apiException)
            {
                httpCode = apiException.HttpStatusCode;
                body = new ClientError(apiException.ErrorNumber, apiException.ErrorCode, apiException.Message);
            }
            else
            {
                body = ClientError.Unexpected();
            }

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
