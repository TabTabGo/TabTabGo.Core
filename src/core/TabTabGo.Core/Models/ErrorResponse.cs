using TabTabGo.Core.Exceptions;

namespace TabTabGo.Core.Models;

/// <summary>
/// Client-facing error body: message, code and error number only.
/// </summary>
/// <param name="message">Client-safe message.</param>
/// <param name="code">Machine-readable error code.</param>
/// <param name="errorNumber">System error number.</param>
/// <param name="exception">Ignored. Exceptions are never exposed to clients; kept for source compatibility.</param>
#pragma warning disable CS9113 // exception is intentionally unread: exceptions are never exposed to clients
public class ErrorResponse(string message, string? code, int? errorNumber = null, Exception? exception = null)
{
    public string Message { get; set; } = message;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; } = code;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ErrorNumber { get; set; } = errorNumber;

    /// <summary>
    /// Always null and never serialised.
    /// </summary>
    [Obsolete("Never populated and never serialised: exposing an exception to API clients leaks internals (stack, types, inner messages). Log it server-side instead. Kept only for source compatibility.")]
    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public Exception? Exception { get; set; }

    /// <summary>
    /// Builds the client body from an <see cref="ApiException"/> (message, code and number only).
    /// </summary>
    public ErrorResponse(ApiException exception) : this(ClientMessage(exception), exception.ErrorCode, exception.ErrorNumber)
    {
    }

    // new ApiException() carries the framework default message ("Exception of type '...' was thrown."), which names the type
    private static string ClientMessage(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) || exception.Message.StartsWith("Exception of type '", StringComparison.Ordinal)
            ? "An unexpected error occurred."
            : exception.Message;
}
#pragma warning restore CS9113
