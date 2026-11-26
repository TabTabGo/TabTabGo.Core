namespace TabTabGo.Core.ViewModels;

/// <summary>
/// Generic error response. The exception property is obsolete: it is never populated and never serialised.
/// </summary>
public class ErrorResponseViewModel : GenericResponseViewModel
{
    /// <summary>Always null and never serialised (D-7): an exception must not reach API clients. Log it server-side instead.</summary>
    [Obsolete("Never populated and never serialised: exposing an exception to API clients leaks internals. Log it server-side instead. Kept only for source compatibility.")]
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public Exception? Exception { get; set; }
}
