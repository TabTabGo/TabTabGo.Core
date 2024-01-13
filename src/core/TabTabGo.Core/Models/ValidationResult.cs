namespace TabTabGo.Core.Services;

public class ValidationResult
{
    public bool IsValid { get; set; }
    public IDictionary<string,IList<String>> Errors { get; set; }  = new Dictionary<string, IList<string>>();
}