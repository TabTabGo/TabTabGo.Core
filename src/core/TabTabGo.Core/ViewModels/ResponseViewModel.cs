namespace TabTabGo.Core.Services.ViewModels;

/// <summary>
/// Outcome wrapper with a payload.
/// </summary>
/// <typeparam name="T">Payload type</typeparam>
public class ResponseViewModel<T>
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Http status code.</summary>
    public int StatusCode { get; set; }

    /// <summary>System code.</summary>
    public string? Code { get; set; }

    /// <summary>Message.</summary>
    public string? Message { get; set; }

    /// <summary>Always null and never serialised (D-7): an exception must not reach API clients. Log it server-side instead.</summary>
    [Obsolete("Never populated and never serialised: exposing an exception to API clients leaks internals. Log it server-side instead. Kept only for source compatibility.")]
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public Exception? Exception { get; set; }

    /// <summary>The payload.</summary>
    public T? Payload { get; set; }
}

/// <summary>
/// Outcome wrapper with an untyped payload.
/// </summary>
public class ResponseViewModel : ResponseViewModel<object>
{
}
