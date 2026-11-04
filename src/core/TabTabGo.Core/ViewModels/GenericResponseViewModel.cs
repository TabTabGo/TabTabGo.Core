namespace TabTabGo.Core.ViewModels;

/// <summary>
/// Status code, system code and message.
/// </summary>
public class GenericResponseViewModel
{
    /// <summary>Http status code.</summary>
    public int StatusCode { get; set; }

    /// <summary>System code.</summary>
    public string? Code { get; set; }

    /// <summary>Message.</summary>
    public string Message { get; set; } = string.Empty;
}
