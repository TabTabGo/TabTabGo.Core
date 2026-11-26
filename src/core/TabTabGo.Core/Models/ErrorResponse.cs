using TabTabGo.Core.Exceptions;
using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Models;

/// <summary>
/// Error body returned by the API. The exception object is never serialized.
/// </summary>
/// <param name="message">Client-safe message.</param>
/// <param name="code">Machine-readable error code.</param>
/// <param name="errorNumber">System error number.</param>
/// <param name="exception">Ignored. Exceptions are never exposed to clients; kept for source compatibility.</param>
#pragma warning disable CS9113 // exception is intentionally unread
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

    /// <summary>Always null and never serialised (D-7). Log the exception server-side instead.</summary>
    [Obsolete("Never populated and never serialised: exposing an exception to API clients leaks internals. Log it server-side instead. Kept only for source compatibility.")]
    [JsonIgnore]
    [Nj.JsonIgnore]
    public Exception? Exception { get; set; }

    /// <summary>Creates an error response from an <see cref="ApiException"/> (message, code and number only).</summary>
    public ErrorResponse(ApiException exception) : this(exception.Message, exception.ErrorCode, exception.ErrorNumber)
    {
    }
}
#pragma warning restore CS9113
