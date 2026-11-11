using Microsoft.Extensions.Localization;
using TabTabGo.Core.Country.Services;
using TabTabGo.Core.Services;

namespace TabTabGo.Core.Test.Services;

public class CountryServiceTests
{
    private static readonly SetupServices Factory = new();
    private readonly ICountryService _countryService = Factory.GetRequiredService<ICountryService>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TestEnglishNameByAlpha2()
    {
        var country = await _countryService.GetCountry("AE", cancellationToken: Ct);

        Assert.Equal("United Arab Emirates", country!.Name);
        Assert.Equal("784", country.Number);
    }

    [Fact]
    public async Task TestArabicNameByAlpha2()
    {
        var country = await _countryService.GetCountry("AE", "ar", Ct);

        Assert.Equal("الإمارات العربية المتحدة", country!.Name);
        Assert.Equal("784", country.Number);
    }

    [Fact]
    public async Task TestEnglishNameByAlpha3()
    {
        var country = await _countryService.GetCountry("ARE", cancellationToken: Ct);

        Assert.Equal("United Arab Emirates", country!.Name);
        Assert.Equal("784", country.Number);
    }

    [Fact]
    public async Task TestArabicNameByAlpha3()
    {
        var country = await _countryService.GetCountry("ARE", "ar", Ct);

        Assert.Equal("الإمارات العربية المتحدة", country!.Name);
        Assert.Equal("784", country.Number);
    }

    [Theory]
    [InlineData("ae")]
    [InlineData("Ae")]
    [InlineData("are")]
    public async Task GetCountry_LowercaseCode_Found(string code)
    {
        // codes were matched case-sensitively, so "ae" was not found (O-13)
        var country = await _countryService.GetCountry(code, cancellationToken: Ct);

        Assert.Equal("AE", country!.Alpha2);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("AR")]
    [InlineData("ar-AE")]
    public async Task GetCountry_ArabicCultureSpellings_AreTranslated(string culture)
    {
        var country = await _countryService.GetCountry("ae", culture, Ct);

        Assert.Equal("الإمارات العربية المتحدة", country!.Name);
    }

    [Theory]
    [InlineData("EN")]
    [InlineData("en-US")]
    [InlineData("")]
    public async Task GetCountry_EnglishCultureSpellings_KeepTheEnglishName(string culture)
    {
        var country = await _countryService.GetCountry("AE", culture, Ct);

        Assert.Equal("United Arab Emirates", country!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ZZ")]
    [InlineData("XXX")]
    public async Task GetCountry_EmptyOrUnknownCode_IsNull(string code)
    {
        Assert.Null(await _countryService.GetCountry(code, cancellationToken: Ct));
    }

    [Fact]
    public async Task GetCountry_UnknownCulture_KeepsTheEnglishName()
    {
        var country = await _countryService.GetCountry("AE", "fr", Ct);

        Assert.Equal("United Arab Emirates", country!.Name);
    }

    [Fact]
    public async Task GetCountry_ReturnsACopy_SoCallersCannotChangeTheSharedData()
    {
        var first = await _countryService.GetCountry("AE", cancellationToken: Ct);
        first!.Name = "changed";

        var second = await _countryService.GetCountry("AE", cancellationToken: Ct);

        Assert.Equal("United Arab Emirates", second!.Name);
    }

    [Fact]
    public async Task GetCountry_NoDataFolder_Works()
    {
        // the data is embedded; nothing is read from the working directory (D-28)
        var original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());

            Assert.NotNull(await _countryService.GetCountry("AE", cancellationToken: Ct));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    [Fact]
    public async Task GetCountry_PlainLocalizer_ReceivesTheCultureAsFormatArgument()
    {
        var service = new CountryService(new PlainLocalizer());

        var translated = await service.GetCountry("AE", "xx", Ct);
        var untranslated = await service.GetCountry("AE", "zz", Ct);

        Assert.Equal("translated-xx", translated!.Name);
        Assert.Equal("United Arab Emirates", untranslated!.Name); // ResourceNotFound keeps the English name
    }

    [Fact]
    public void Constructor_NullLocalizer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CountryService(null!));
    }

    [Fact]
    public async Task GetCountry_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _countryService.GetCountry("AE", "en", cts.Token));
    }

    private sealed class PlainLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, true);

        public LocalizedString this[string name, params object[] arguments] =>
            (string)arguments[0] == "xx" ? new LocalizedString(name, "translated-xx", false) : new LocalizedString(name, name, true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }
}
