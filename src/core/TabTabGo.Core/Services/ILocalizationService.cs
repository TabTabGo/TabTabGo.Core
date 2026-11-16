using System.Globalization;

namespace TabTabGo.Core.Services;

/// <summary>
/// Tells the current language of the request.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Two-letter language of the current request.</summary>
    string GetCurrentLanguage();

    /// <summary>Culture of the current request.</summary>
    CultureInfo GetCurrentCulture();

    /// <summary>Language for a culture code.</summary>
    /// <param name="cultureCode">Culture code, for example "ar-AE"</param>
    string GetLanguageByCultureCode(string cultureCode);
}
