namespace TabTabGo.Core.Services.ViewModels;

public class ResponseViewModel<T>
{
    public bool Succeeded { get; set; }
    public int StatusCode { get; set; }
    public string Code { get; set; }
    public string Message { get; set; }
    public Exception Exception { get; set; }
    public T Payload { get; set; }
}

public class ResponseViewModel : ResponseViewModel<object>
{

}

