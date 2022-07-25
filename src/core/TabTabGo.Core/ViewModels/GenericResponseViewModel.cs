namespace TabTabGo.Core.ViewModels;

public class GenericResponseViewModel
{
    public int StatusCode { get; set; }

    public string? Code { get; set; }

    public string Message { get; set; } = string.Empty;
}