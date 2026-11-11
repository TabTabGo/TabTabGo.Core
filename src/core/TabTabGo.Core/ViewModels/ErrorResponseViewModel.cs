namespace TabTabGo.Core.ViewModels;

/// <summary>
/// Generic response with the exception that caused the error.
/// </summary>
public class ErrorResponseViewModel : GenericResponseViewModel
{
    /// <summary>The exception.</summary>
    public Exception? Exception { get; set; }
}
