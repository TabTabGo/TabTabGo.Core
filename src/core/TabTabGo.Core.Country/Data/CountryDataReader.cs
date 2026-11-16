using System.Reflection;
using System.Text.Json;
using CountryModel = TabTabGo.Core.Models.Country;

namespace TabTabGo.Core.Country.Data;

/// <summary>
/// Reads the country list that ships inside this assembly (embedded resource), parses it once per process and hands out
/// copies, so callers can change what they get.
/// </summary>
public static class CountryDataReader
{
    private const string ResourceName = "TabTabGo.Core.Country.Data.countries_info.json";

    private static readonly Lazy<IReadOnlyList<CountryModel>> Countries = new(Load);

    /// <summary>Gets a fresh copy of every country.</summary>
    public static IReadOnlyList<CountryModel> GetCountries()
    {
        return Countries.Value.Select(Copy).ToList();
    }

    /// <summary>Gets a fresh copy of every country.</summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public static Task<IReadOnlyList<CountryModel>> GetCountriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetCountries());
    }

    private static IReadOnlyList<CountryModel> Load()
    {
        using var stream = typeof(CountryDataReader).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        return JsonSerializer.Deserialize<List<CountryModel>>(stream)
               ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is empty.");
    }

    private static CountryModel Copy(CountryModel source) => new()
    {
        Name = source.Name,
        Alpha2 = source.Alpha2,
        Alpha3 = source.Alpha3,
        Number = source.Number,
        OfficialName = source.OfficialName,
        PhoneCode = source.PhoneCode,
    };
}
