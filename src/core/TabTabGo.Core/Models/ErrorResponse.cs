using TabTabGo.Core.Exceptions;
using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Models;

/// <summary>
/// Error body returned by the API. The exception object is never serialized.
/// </summary>
public class ErrorResponse(string message, string? code, int? errorNumber = null, Exception? exception = null)
{
    /// <summary>Human readable message.</summary>
    public string Message { get; set; } = message;

    /// <summary>System error code.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; } = code;

    /// <summary>System error number.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ErrorNumber { get; set; } = errorNumber;

    /// <summary>The exception that caused the error. Kept for logging; never part of the response body (D-7).</summary>
    [JsonIgnore]
    [Nj.JsonIgnore]
    public Exception? Exception { get; set; } = exception;

    /// <summary>Creates an error response from an <see cref="ApiException"/>.</summary>
    public ErrorResponse(ApiException exception) : this(exception.Message, exception.ErrorCode, exception.ErrorNumber, exception)
    {
    }
}
