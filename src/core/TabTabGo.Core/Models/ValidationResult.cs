namespace TabTabGo.Core.Services;

/// <summary>
/// Outcome of a validation: valid flag and the messages per member.
/// </summary>
public class ValidationResult
{
    /// <summary>True when there are no errors.</summary>
    public bool IsValid { get; set; }

    /// <summary>Error messages keyed by member name.</summary>
    public IDictionary<string, IList<string>> Errors { get; set; } = new Dictionary<string, IList<string>>();
}
