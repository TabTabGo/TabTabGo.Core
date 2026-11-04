using System.Globalization;
using Microsoft.Extensions.Options;

namespace TabTabGo.Core.Services.Tests;

public sealed class JsonStringLocalizerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ttg-loc-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside;

    public JsonStringLocalizerTests()
    {
        Directory.CreateDirectory(_root);
        // a secret file next to the resources folder that must never be reachable through a culture name
        _outside = Path.Combine(Path.GetDirectoryName(_root)!, "ttg-secret-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(_outside, "{\"Key\":\"leaked\"}");
        File.WriteAllText(Path.Combine(_root, "en.json"), "{\"Hello\":\"Hello\",\"Fmt\":\"Hi {0}\",\"Nested\":{\"Deep\":\"deep value\"}}");
        File.WriteAllText(Path.Combine(_root, "ar-AE.json"), "{\"Hello\":\"marhaba\"}");
        File.WriteAllText(Path.Combine(_root, "fr.json"), "{\"Hello\":\"bonjour\"}");
        File.WriteAllText(Path.Combine(_root, "fr-CA.json"), "{\"Only\":\"canadian\"}");
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
        File.Delete(_outside);
    }

    private JsonStringLocalizer Create(string? embeddedAssemblyName = "") => new(new JsonStringLocalizerOptions
    {
        ResourcesPath = _root,
        ResourceAssemblyName = embeddedAssemblyName ?? "",
    });

    private static string En(JsonStringLocalizer l, string name) => l.GetString(name, "en");

    [Fact]
    public void GetString_Culture_ReturnsThatCulture()
    {
        var l = Create();

        Assert.Equal("marhaba", l.GetString("Hello", "ar-AE"));
        Assert.Equal("Hello", l.GetString("Hello", "en"));
    }

    [Fact]
    public void GetString_NeutralCultureFindsSpecificFileOfTheSameLanguage()
    {
        Assert.Equal("marhaba", Create().GetString("Hello", "ar"));
    }

    [Fact]
    public void GetString_ParentCultureIsUsedWhenSpecificFileHasNoKey()
    {
        var l = Create();

        Assert.Equal("bonjour", l.GetString("Hello", "fr-CA")); // fr-CA lacks Hello, parent fr has it
        Assert.Equal("canadian", l.GetString("Only", "fr-CA"));
    }

    [Fact]
    public void GetString_UnknownCultureFallsBackToDefault_MissingKeyReturnsName()
    {
        var l = Create();

        Assert.Equal("Hello", l.GetString("Hello", "de"));
        Assert.Equal("Nope", l.GetString("Nope", "en"));
    }

    [Fact]
    public void Indexers_UseCurrentUICultureAndFormatArguments()
    {
        var l = Create();
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar-AE");
            Assert.Equal("marhaba", l["Hello"].Value);
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.Equal("Hi Sam", l["Fmt", "Sam"].Value); // args are formatted, not read as a culture
            Assert.False(l["Fmt"].ResourceNotFound);
            Assert.True(l["Missing"].ResourceNotFound);
            Assert.Equal("Missing", l["Missing"].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void NestedKeys_AreLookedUpByDottedPath()
    {
        Assert.Equal("deep value", En(Create(), "Nested.Deep"));
    }

    [Fact]
    public void GetAllStrings_ReturnsEntriesOfTheCultureChain()
    {
        var l = Create();
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("fr-CA");
            var all = l.GetAllStrings(true).ToDictionary(s => s.Name, s => s.Value);

            Assert.Equal("canadian", all["Only"]);
            Assert.Equal("bonjour", all["Hello"]);
            Assert.Equal("Hi {0}", all["Fmt"]);
            Assert.DoesNotContain("Only", l.GetAllStrings(false).Select(s => s.Name).Except(new[] { "Only" }));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void FileOverride_WinsOverEmbedded()
    {
        var embeddedOnly = new JsonStringLocalizer(new JsonStringLocalizerOptions
        {
            ResourceAssembly = typeof(JsonStringLocalizerTests).Assembly,
            ResourcesPath = Path.Combine(_root, "none"),
        });
        // the embedded Country data is not in this assembly, so build the override case with the Country assembly
        var country = typeof(TabTabGo.Core.Country.Services.CountryService).Assembly;
        var overrideDir = Path.Combine(_root, "override");
        Directory.CreateDirectory(overrideDir);
        File.WriteAllText(Path.Combine(overrideDir, "ar-AE.json"), "{\"AE\":\"override\"}");

        var withOverride = new JsonStringLocalizer(new JsonStringLocalizerOptions { ResourceAssembly = country, ResourcesPath = overrideDir });
        var plain = new JsonStringLocalizer(new JsonStringLocalizerOptions { ResourceAssembly = country, ResourcesPath = Path.Combine(_root, "none") });

        Assert.Equal("override", withOverride.GetString("AE", "ar-AE"));
        Assert.NotEqual("override", plain.GetString("AE", "ar-AE"));
        Assert.NotEqual("AE", plain.GetString("AE", "ar-AE")); // the embedded entry exists
        Assert.NotNull(embeddedOnly);
    }

    [Fact]
    public void WorkingDirectoryIndependent()
    {
        var original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Assert.Equal("marhaba", Create().GetString("Hello", "ar-AE"));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    [Fact]
    public void Parallel_LookupsParseEachFileOnce()
    {
        var l = Create();
        var before = JsonStringLocalizer.ParseCount;

        Parallel.For(0, 100, _ => Assert.Equal("Hello", l.GetString("Hello", "en")));

        Assert.Equal(1, JsonStringLocalizer.ParseCount - before);
    }

    [Fact]
    public void Constructors_DistributedCacheIsIgnored_OptionsAreHonoured_NullThrows()
    {
        var options = Options.Create(new JsonStringLocalizerOptions { ResourcesPath = _root, ResourceAssemblyName = "" });

        Assert.Equal("marhaba", new JsonStringLocalizer(null, options).GetString("Hello", "ar-AE"));
        Assert.Throws<ArgumentNullException>(() => new JsonStringLocalizer((JsonStringLocalizerOptions)null!));
        Assert.NotNull(new JsonStringLocalizer());
    }

    [Fact]
    public void Indexer_NullName_Throws()
    {
        var l = Create();

        Assert.Throws<ArgumentNullException>(() => l[null!]);
        Assert.Throws<ArgumentNullException>(() => l.GetString(null!, "en"));
    }

    // --- culture validation and path traversal (D-36) ------------------------------------------------------------

    [Theory]
    [InlineData("../ttg-secret")]
    [InlineData("..\\ttg-secret")]
    [InlineData("..")]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win")]
    [InlineData("ar/../en")]
    [InlineData("en\0")]
    [InlineData("en.json")]
    [InlineData("a")]
    [InlineData("en_US")]
    [InlineData("en-")]
    [InlineData("-en")]
    [InlineData("en--US")]
    [InlineData("en US")]
    [InlineData("\u0627\u0644")]
    public void GetString_MaliciousOrMalformedCulture_FallsBackToDefaultAndNeverReadsOtherFiles(string culture)
    {
        var l = Create();

        Assert.Equal("Hello", l.GetString("Hello", culture)); // default culture answer
        Assert.NotEqual("leaked", l.GetString("Key", culture));
        Assert.Equal("Key", l.GetString("Key", culture));
    }

    [Fact]
    public void GetString_NullEmptyAndOverlongCulture_UseDefault()
    {
        var l = Create();

        Assert.Equal("Hello", l.GetString("Hello", null!));
        Assert.Equal("Hello", l.GetString("Hello", ""));
        Assert.Equal("Hello", l.GetString("Hello", new string('a', 10_000)));
        Assert.Equal("Hello", l.GetString("Hello", "en-" + new string('x', 40)));
    }

    [Fact]
    public void Cache_OnlyGrowsForExistingResources()
    {
        var l = Create();
        l.GetString("Hello", "en"); // make sure the known entries exist
        var before = JsonStringLocalizer.CacheCount;

        for (var i = 0; i < 500; i++)
        {
            l.GetString("Hello", $"zz-{i:0000}");
            l.GetString("Hello", "../x" + i);
        }

        Assert.Equal(before, JsonStringLocalizer.CacheCount);
    }

    [Fact]
    public void Cache_CaseVariantsOfAnExistingCultureShareOneEntry()
    {
        var l = Create();
        l.GetString("Hello", "ar-AE");
        var before = JsonStringLocalizer.CacheCount;

        foreach (var variant in new[] { "ar-ae", "AR-AE", "Ar-aE" })
        {
            Assert.Equal("marhaba", l.GetString("Hello", variant));
        }

        Assert.Equal(before, JsonStringLocalizer.CacheCount);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("ar-AE", true)]
    [InlineData("zh-Hans-CN", true)]
    [InlineData("es-419", true)]
    [InlineData("fil", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("e", false)]
    [InlineData("english", false)]
    [InlineData("en/US", false)]
    [InlineData("en-US-", false)]
    [InlineData("en-U", false)]
    public void IsValidCultureName_AcceptsOnlyCultureShapedNames(string? culture, bool expected)
    {
        Assert.Equal(expected, JsonStringLocalizer.IsValidCultureName(culture));
    }

    [Fact]
    public void Options_Defaults()
    {
        var options = new JsonStringLocalizerOptions();

        Assert.Equal("en", options.DefaultCulture);
        Assert.Equal("TabTabGo.Core.Country", options.ResourceAssemblyName);
        Assert.Null(options.ResourceAssembly);
        Assert.StartsWith(AppContext.BaseDirectory, options.ResourcesPath);
    }
}
