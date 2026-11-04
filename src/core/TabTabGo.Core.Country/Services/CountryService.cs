using Microsoft.Extensions.Localization;
using TabTabGo.Core.Country.Data;
using TabTabGo.Core.Services;

namespace TabTabGo.Core.Country.Services;

/// <summary>
/// Finds countries by ISO code and returns their name in English or in the language of the localizer.
/// </summary>
public class CountryService : ICountryService
{
    private readonly IStringLocalizer _stringLocalizer;

    /// <summary>Creates the service.</summary>
    /// <param name="stringLocalizer">Localizer that holds the translated country names, keyed by upper-case ISO code</param>
    public CountryService(IStringLocalizer stringLocalizer)
    {
        ArgumentNullException.ThrowIfNull(stringLocalizer);
        _stringLocalizer = stringLocalizer;
    }

    /// <inheritdoc />
    public async Task<Models.Country?> GetCountry(string code, string culture = "en", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        var countries = await CountryDataReader.GetCountriesAsync(cancellationToken);
        var country = countries.FirstOrDefault(c =>
            string.Equals(c.Alpha2, code, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Alpha3, code, StringComparison.OrdinalIgnoreCase));
        if (country is not null && !IsEnglish(culture))
        {
            var key = code.ToUpperInvariant();
            var translated = Translate(key, culture);
            if (!string.IsNullOrEmpty(translated))
            {
                country.Name = translated;
            }
        }

        return country;
    }

    private static bool IsEnglish(string? culture) =>
        string.IsNullOrEmpty(culture) || culture.Equals("en", StringComparison.OrdinalIgnoreCase) ||
        culture.StartsWith("en-", StringComparison.OrdinalIgnoreCase);

    private string? Translate(string key, string culture)
    {
        if (_stringLocalizer is ICultureStringLocalizer cultureLocalizer)
        {
            var value = cultureLocalizer.GetString(key, culture);
            return value == key ? null : value;
        }

        // plain localizers: the culture travels as the first format argument (the convention of the old JsonStringLocalizer)
        var localized = _stringLocalizer[key, culture];
        return localized.ResourceNotFound ? null : localized.Value;
    }
}
