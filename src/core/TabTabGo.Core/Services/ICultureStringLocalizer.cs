namespace TabTabGo.Core.Services;

/// <summary>
/// Looks up localized strings for an explicit culture instead of the current thread culture.
/// </summary>
public interface ICultureStringLocalizer
{
    /// <summary>Gets a string; the name itself is returned when no translation exists.</summary>
    /// <param name="name">Resource name</param>
    /// <param name="culture">Culture name, for example "ar" or "ar-AE"</param>
    /// <param name="arguments">Format arguments</param>
    string GetString(string name, string culture, params object[] arguments);
}
