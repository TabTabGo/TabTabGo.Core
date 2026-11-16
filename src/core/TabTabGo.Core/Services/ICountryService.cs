using TabTabGo.Core.Models;

namespace TabTabGo.Core.Services;

/// <summary>
/// Looks up country reference data.
/// </summary>
public interface ICountryService
{
    /// <summary>Finds a country by its ISO alpha-2 or alpha-3 code (case-insensitive).</summary>
    /// <param name="code">Alpha-2 or alpha-3 code</param>
    /// <param name="culture">Language of the name, "en" or "ar"</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The country, or null when the code is unknown</returns>
    Task<Country?> GetCountry(string code, string culture = "en", CancellationToken cancellationToken = default);
}
