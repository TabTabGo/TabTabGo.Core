namespace TabTabGo.Core.ViewModels;

/// <summary>
/// A custom OData filter function call: its name and arguments.
/// </summary>
public class CustomOdataFiler
{
    /// <summary>Function name.</summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>Function arguments.</summary>
    public List<string> Parameters { get; set; } = new();
}
