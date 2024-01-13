using TabTabGo.Core.Exceptions;

namespace TabTabGo.Core.Models;

public class ErrorResponse(string message, string? code, int? errorNumber = null, Exception? exception = null)
{
    public string Message { get; set; } = message;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; } = code;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ErrorNumber { get; set; } = errorNumber;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Exception? Exception { get; set; } = exception;

    public ErrorResponse(ApiException exception) : this(exception.Message, exception.ErrorCode, exception.ErrorNumber, exception)
    {
    }
}