namespace TabTabGo.Core.WebApi.Middlewares;

/// <summary>
/// The only error shape sent to clients for unexpected failures: number, code and a generic message.
/// Never carries exception text, stack traces or type names; the full exception is logged server-side.
/// </summary>
/// <param name="Number">System error number (0 when unknown).</param>
/// <param name="Code">Machine-readable error code.</param>
/// <param name="Message">Client-safe message.</param>
internal sealed record ClientError(int Number, string? Code, string Message)
{
    /// <summary>Generic message used for every unhandled failure.</summary>
    public const string GenericMessage = "An unexpected error occurred.";

    /// <summary>Creates the generic 500 body for the given code.</summary>
    public static ClientError Unexpected(string? code = null, string? message = null)
        => new(0, code, message ?? GenericMessage);
}
