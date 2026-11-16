using System.Net;
using Microsoft.Extensions.Logging;

namespace TabTabGo.Core.Exceptions;

/// <summary>
/// Exception to be thrown by the API
/// </summary>
[Obsolete("Use ApiException instead")]
public class TTGException : Exception
{
    /// <summary>System error number.</summary>
    public int ErrorNumber { get; private set; }

    /// <summary>Http status code to be returned.</summary>
    public HttpStatusCode HttpStatusCode { get; private set; }

    /// <summary>Innermost exception.</summary>
    public Exception? RootException { get; private set; }

    /// <summary>System error code.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Error level.</summary>
    public LogLevel Level { get; private set; }

    /// <summary>Creates an empty exception.</summary>
    public TTGException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public TTGException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and cause.</summary>
    public TTGException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Creates an exception with every field.</summary>
    public TTGException(string message, Exception? inner = null, LogLevel level = LogLevel.Error, int? errorNumber = null, HttpStatusCode statusCode = HttpStatusCode.InternalServerError, string? code = null)
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
