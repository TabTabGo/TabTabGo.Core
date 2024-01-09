using TabTabGo.Core.Country;
using TabTabGo.Core.Country.Services;

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
    public async void TestEnglishName()
    {
        var code = "AE";
        var name = await _countryService.GetCountryName(code);
        Assert.Equal("United Arab Emirates", name);
    }
    [Fact]
    public async void TestArabicName()
    {
        var code = "AE";
        var name = await _countryService.GetCountryName(code , "ar");
        Assert.Equal("الامارات العربية المتحدة", name);
    }
}