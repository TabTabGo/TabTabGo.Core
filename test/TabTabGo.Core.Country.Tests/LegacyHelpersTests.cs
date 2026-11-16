using Nma.Lms.Test.Helpers;

namespace TabTabGo.Core.Test;

#pragma warning disable CS0618 // the legacy helpers are obsolete on purpose; they are tested until they are removed in 11.x
public sealed class LegacyHelpersTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ttg-legacy-" + Guid.NewGuid().ToString("N"));

    public LegacyHelpersTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record Row(string Name, int Count);

    [Fact]
    public async Task ReadData_GetCountries_DelegatesToTheEmbeddedData()
    {
        var countries = await ReadData.GetCountries(Ct);

        Assert.Equal(248, countries.Count);
    }

    [Theory]
    [InlineData("a-b-c", "-", "a")]
    [InlineData("abc", "-", "")]
    [InlineData("", "-", "")]
    [InlineData("  ", "-", "")]
    [InlineData("-abc", "-", "")]
    public void Tools_GetUntilOrEmpty(string text, string stopAt, string expected)
    {
        Assert.Equal(expected, text.GetUntilOrEmpty(stopAt));
        Assert.Equal("x", "x-y".GetUntilOrEmpty());
    }

    [Fact]
    public void FileHelper_SerializeDeserialize()
    {
        var json = FileHelper.SerializeObj(new Row("a", 1));

        Assert.Equal(new Row("a", 1), FileHelper.DeserializeObj<Row>(json));
        Assert.Throws<System.Text.Json.JsonException>(() => FileHelper.DeserializeObj<Row>("null"));
    }

    [Fact]
    public async Task FileHelper_AddUpdateAndReadFiles_WithAsyncIo()
    {
        var path = Path.Combine(_folder, "rows.json");

        await FileHelper.AddDataToJsonArrayInFile<Row>(path, "{\"Name\":\"first\",\"Count\":1}");
        await FileHelper.AddDataToJsonArrayInFile<Row>(path, "{\"Name\":\"second\",\"Count\":2}");
        var rows = await FileHelper.FileToObject<List<Row>>(path);

        Assert.Equal(new[] { "second", "first" }, rows.Select(r => r.Name)); // the new item goes first
        Assert.True(FileHelper.IsExist(path));
        Assert.Contains("second", await FileHelper.ReadJsonFile(path));

        await FileHelper.UpdateDataToJsonArrayInFile<Row>(path, "[{\"Name\":\"only\",\"Count\":9}]");
        Assert.Single(await FileHelper.FileToObject<List<Row>>(path));
    }

    [Fact]
    public async Task FileHelper_ConcurrentWrites_DoNotLoseItems()
    {
        var path = Path.Combine(_folder, "concurrent.json");

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => FileHelper.AddDataToJsonArrayInFile<Row>(path, $"{{\"Name\":\"n{i}\",\"Count\":{i}}}")));

        Assert.Equal(10, (await FileHelper.FileToObject<List<Row>>(path)).Count);
    }

    [Fact]
    public async Task FileHelper_RelativePaths_AreResolvedAgainstTheApplicationFolder()
    {
        var relative = "ttg-relative-" + Guid.NewGuid().ToString("N") + ".json";
        var full = Path.Combine(AppContext.BaseDirectory, relative);
        try
        {
            await FileHelper.UpdateDataToJsonArrayInFile<Row>(relative, "[]");

            Assert.True(File.Exists(full));
            Assert.True(FileHelper.IsExist(relative));
        }
        finally
        {
            File.Delete(full);
        }
    }

    [Fact]
    public void FileHelper_IsFileReady_FalseWhenMissingEmptyOrLocked()
    {
        var path = Path.Combine(_folder, "ready.txt");

        Assert.False(FileHelper.IsFileReady(path));
        File.WriteAllText(path, string.Empty);
        Assert.False(FileHelper.IsFileReady(path));
        File.WriteAllText(path, "x");
        Assert.True(FileHelper.IsFileReady(path));
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.False(FileHelper.IsFileReady(path));
    }

    [Fact]
    public void FileHelper_GetRootPath_StopsAtBin()
    {
        var root = FileHelper.GetRootPath();

        Assert.DoesNotContain("bin", root);
        Assert.NotEmpty(root);
    }
}
