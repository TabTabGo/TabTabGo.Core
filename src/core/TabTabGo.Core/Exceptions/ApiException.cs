using System.Net;
using Microsoft.Extensions.Logging;

namespace TabTabGo.Core.Exceptions;


/// <summary>
/// Api exception that will be handled in controller and return a message , cod and http status code response instead of normal exception
/// </summary>
[Serializable]
public class ApiException : Exception
{
    /// <summary>
    /// System error number
    /// </summary>
    public int ErrorNumber { get; private set; } = 0;

    /// <summary>
    /// Http status code to be returned
    /// </summary>
    public HttpStatusCode HttpStatusCode { get; private set; } = HttpStatusCode.InternalServerError;

    /// <summary>
    /// Inner exception that caused this exception
    /// </summary>
    public Exception? RootException { get; private set; }
    /// <summary>
    /// System Error code to be returned
    /// </summary>
    public string? ErrorCode { get; set; }
    /// <summary>
    /// Error level
    /// </summary>
    public LogLevel Level { get; private set; } = LogLevel.Error;
   
    /// <summary>
    /// Constructor for ApiException
    /// </summary>
    public ApiException()
    {
    }

    /// <summary>
    /// Constructor for ApiException with message
    /// </summary>
    /// <param name="message">Error message</param>
    public ApiException(string message)
        : base(message)
    {

    }

    /// <summary>
    /// Constructor for ApiException with message and inner exception
    /// </summary>
    /// <param name="message">custom error message</param>
    /// <param name="inner">cause of issue</param>
    public ApiException(string message, Exception inner)
        : base(message, inner)
    {

    }

    /// <summary>
    /// Constructor for ApiException with message, inner exception, error level, error number, http status code and error code
    /// </summary>
    /// <param name="message">Custom error message</param>
    /// <param name="inner">Internal exception</param>
    /// <param name="level">Exception level</param>
    /// <param name="errorNumber">Exception Error number</param>
    /// <param name="statusCode">Http response status code</param>
    /// <param name="code">System error code</param>
    public ApiException(string message, Exception? inner = null, LogLevel level = LogLevel.Error, int? errorNumber = null, HttpStatusCode statusCode = HttpStatusCode.InternalServerError, string code = null)
        : base(message, inner)
    {
        if (inner != null)
        {
            var rootException = inner;
            while (rootException.InnerException != null)
            {
                rootException = rootException.InnerException;
            }

            this.RootException = rootException;
        }
        this.Level = level;
        if (errorNumber.HasValue)
        {
            this.ErrorNumber = errorNumber.Value;
        }
        this.ErrorCode = code;
        this.HttpStatusCode = statusCode;
    }
}

