using Microsoft.AspNetCore.Http;
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

        public ErrorHandlingMiddleware(RequestDelegate next)
        {
            this.next = next;
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

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var httpCode = HttpStatusCode.InternalServerError; // 500 if unexpected
            string errorCode = null;
            int errorNumber;
            // if (exception is TabTabGo.Core.ApiException)
            // {
            //     httpCode = (exception as ApiException).HttpStatusCode;
            //     errorNumber = (exception as ApiException).ErrorNumber;
            //     errorCode = (exception as ApiException).ErrorCode;
            // }
            //else if (exception is NotFoundException) code = HttpStatusCode.Unauthorized;
            //else if (exception is MyException) code = HttpStatusCode.BadRequest;

            var result = JsonConvert.SerializeObject(new { number = errorCode, code = errorCode, message = exception.Message , exception = exception,  });
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)httpCode;
            return context.Response.WriteAsync(result);
        }
    }
}
