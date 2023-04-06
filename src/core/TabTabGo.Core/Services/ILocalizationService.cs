using System.Globalization;

namespace TabTabGo.Core.Services;
public interface ILocalizationService
{
    string GetCurrentLanguage();
    CultureInfo GetCurrentCulture();
    string GetLanguageByCultureCode(string cultureCode);
}
