using TabTabGo.Core.Country;
using TabTabGo.Core.Country.Services;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services;

namespace TabTabGo.Core.Test.Services;

public class CountryServiceTests
{

    private readonly ICountryService _countryService;
    private static SetupServices factory = null;
    public CountryServiceTests()
    {
        if (factory == null)
            factory = new SetupServices();
        _countryService = factory.GetRequiredService<ICountryService>();
    }
    [Fact]
    public async void TestEnglishNameByAlpha2()
    {
        var code = "AE";
        var country = await _countryService.GetCountryName(code);
        Assert.Equal("United Arab Emirates", country.Name);
        Assert.Equal("784", country.Number);
    }
    [Fact]
    public async void TestArabicNameByAlpha2()
    {
        var code = "AE";
        var country = await _countryService.GetCountryName(code , "ar");
        Assert.Equal("الإمارات العربية المتحدة", country.Name);
        Assert.Equal("784", country.Number);
    }
    [Fact]
    public async void TestEnglishNameByAlpha3()
    {
        var code = "ARE";
        var country = await _countryService.GetCountryName(code);
        Assert.Equal("United Arab Emirates", country.Name);
        Assert.Equal("784", country.Number);
    }
    [Fact]
    public async void TestArabicNameByAlpha3()
    {
        var code = "ARE";
        var country = await _countryService.GetCountryName(code, "ar");
        Assert.Equal("الإمارات العربية المتحدة", country.Name);
        Assert.Equal("784", country.Number);
    }
}