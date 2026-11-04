using TabTabGo.Core.Country.Data;

namespace TabTabGo.Core.Test;

public class CountryDataReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void GetCountries_ReturnsTheEmbeddedList()
    {
        var countries = CountryDataReader.GetCountries();

        Assert.Equal(248, countries.Count);
        Assert.Contains(countries, c => c.Alpha2 == "AE" && c.Alpha3 == "ARE" && c.Number == "784");
        Assert.Equal(countries.Count, countries.Select(c => c.Alpha2).Distinct().Count());
    }

    [Fact]
    public async Task GetCountriesAsync_Works_AndHonoursCancellation()
    {
        Assert.Equal(CountryDataReader.GetCountries().Count, (await CountryDataReader.GetCountriesAsync(Ct)).Count);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CountryDataReader.GetCountriesAsync(cts.Token));
    }

    [Fact]
    public void GetCountries_ReturnsCopies_EveryCall()
    {
        var first = CountryDataReader.GetCountries();
        first[0].Name = "mutated";
        first[0].OfficialName = "mutated";
        first[0].PhoneCode = "mutated";

        var second = CountryDataReader.GetCountries();

        Assert.NotSame(first[0], second[0]);
        Assert.NotEqual("mutated", second[0].Name);
        Assert.NotEqual("mutated", second[0].OfficialName);
        Assert.NotEqual("mutated", second[0].PhoneCode);
    }

    [Fact]
    public void EveryCountryHasAnArabicName()
    {
        var localizer = new TabTabGo.Core.Services.JsonStringLocalizer();

        var missing = CountryDataReader.GetCountries().Where(c => localizer.GetString(c.Alpha2, "ar") == c.Alpha2).Select(c => c.Alpha2).ToList();

        Assert.Empty(missing);
    }
}
