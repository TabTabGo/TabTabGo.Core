namespace TabTabGo.Core.ViewModels;

public class ErrorResponseViewModel : GenericResponseViewModel
{
    /// <summary>
    /// Always null and never serialised.
    /// </summary>
    [Obsolete("Never populated and never serialised: exposing an exception to API clients leaks internals (stack, types, inner messages). Log it server-side instead. Kept only for source compatibility.")]
    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public Exception? Exception { get; set; }
}
