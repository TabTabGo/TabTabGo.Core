namespace TabTabGo.Core.Services.Tests;

/// <summary>
/// JsonStringLocalizer keeps a process-wide cache and parse counter. Every test that reads <c>ParseCount</c> or <c>CacheCount</c>
/// belongs to this collection, which runs on its own: no other test creates a localizer while one of them measures.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizerCacheCollection
{
    public const string Name = "JsonStringLocalizer process-wide cache";
}

[Collection(LocalizerCacheCollection.Name)]
public class LocalizerAllowListTests
{
    [Fact]
    public void Localizer_Layer3_AllowList_OnlyKnowsResourcesThatExisted_WhenFirstListed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ttg-loc3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "en.json"), "{\"Hello\":\"Hello\"}");
        try
        {
            var localizer = new JsonStringLocalizer(new JsonStringLocalizerOptions { ResourcesPath = root, ResourceAssemblyName = string.Empty });
            Assert.Equal("Hello", localizer.GetString("Hello", "en")); // lists the folder once

            File.WriteAllText(Path.Combine(root, "zz.json"), "{\"Hello\":\"late\"}"); // well-shaped, exists, but was not in the list
            var before = JsonStringLocalizer.CacheCount;

            Assert.Equal("Hello", localizer.GetString("Hello", "zz"));
            Assert.Equal(before, JsonStringLocalizer.CacheCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
