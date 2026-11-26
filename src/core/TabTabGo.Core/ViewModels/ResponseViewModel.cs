namespace TabTabGo.Core.Services.ViewModels;

public class ResponseViewModel<T>
{
    public bool Succeeded { get; set; }
    public int StatusCode { get; set; }
    public string Code { get; set; }
    public string Message { get; set; }
    /// <summary>
    /// Always null and never serialised.
    /// </summary>
    [Obsolete("Never populated and never serialised: exposing an exception to API clients leaks internals (stack, types, inner messages). Log it server-side instead. Kept only for source compatibility.")]
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public Exception Exception { get; set; }
    public T Payload { get; set; }
}

public class ResponseViewModel : ResponseViewModel<object>
{

}

