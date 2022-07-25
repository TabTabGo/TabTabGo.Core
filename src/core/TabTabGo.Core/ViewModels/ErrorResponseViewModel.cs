namespace TabTabGo.Core.ViewModels;

public class ErrorResponseViewModel : GenericResponseViewModel
{
    public Exception? Exception { get; set; }
}