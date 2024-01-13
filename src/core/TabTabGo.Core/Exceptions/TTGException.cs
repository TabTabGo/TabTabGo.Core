using System.Net;
using Microsoft.Extensions.Logging;

namespace TabTabGo.Core.Exceptions;


/// <summary>
/// Exception to be thrown by the API
/// </summary>
// deprecated
[Obsolete("Use ApiException instead")]
public class TTGException : Exception
{
    public int ErrorNumber { get; private set; }

    public HttpStatusCode HttpStatusCode { get; private set; }

    public Exception RootException { get; private set; }
    public string ErrorCode { get; set; }
    public LogLevel Level { get; private set; }
   
    public TTGException()
    {
    }

    public TTGException(string message)
        : base(message)
    {

    }

    public TTGException(string message, Exception inner)
        : base(message, inner)
    {

    }


    public TTGException(string message, Exception? inner = null, LogLevel level = LogLevel.Error, int? errorNumber = null, HttpStatusCode statusCode = HttpStatusCode.InternalServerError, string code = null)
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

