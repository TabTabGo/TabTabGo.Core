using TabTabGo.Core.Country.Data;
using TabTabGo.Core.Models;

namespace Nma.Lms.Test.Helpers;

/// <summary>
/// Legacy country reader. Use <see cref="CountryDataReader"/>; this type is removed in 11.x.
/// </summary>
[Obsolete("Use TabTabGo.Core.Country.Data.CountryDataReader. Removed in 11.x.")]
public static class ReadData
{
    /// <summary>Gets the countries (now from the embedded data, no Data folder needed).</summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task<List<Country>> GetCountries(CancellationToken cancellationToken = default)
        => (await CountryDataReader.GetCountriesAsync(cancellationToken)).ToList();
}
