using System.Text;
using TabTabGo.IO.Utilities;

namespace TabTabGo.IO.Utilities.Tests;

public sealed class FileUtilityTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ttg-io-" + Guid.NewGuid().ToString("N"));

    public FileUtilityTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- GetTempFile / CreateTempFile -----------------------------------------------------------------------------

    [Theory]
    [InlineData(".txt")]
    [InlineData("txt")]
    public void GetTempFile_AddsTheDotWhenMissing(string extension)
    {
        var path = FileUtility.GetTempFile(_folder, extension);

        Assert.Equal(_folder, Path.GetDirectoryName(path));
        Assert.StartsWith("ttg-", Path.GetFileName(path));
        Assert.EndsWith(".txt", path);
        Assert.False(File.Exists(path));
        Assert.NotEqual(path, FileUtility.GetTempFile(_folder, extension));
        Assert.DoesNotContain("..", path);
    }

    [Fact]
    public void GetTempFile_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => FileUtility.GetTempFile(null!, "txt"));
        Assert.Throws<ArgumentNullException>(() => FileUtility.GetTempFile(_folder, null!));
    }

    [Fact]
    public void CreateTempFile_WritesContent_NullContentOnlyReturnsAPath()
    {
        var path = FileUtility.CreateTempFile(_folder, "bin", new byte[] { 1, 2, 3 });
        var none = FileUtility.CreateTempFile(_folder, "bin", null);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(none));
    }

#pragma warning disable CS0618 // the pdf-only overload is obsolete on purpose
    [Fact]
    public void CreateTempFile_ObsoletePdfOverload_StillWorks()
    {
        var path = FileUtility.CreateTempFile(_folder, new byte[] { 9 });

        Assert.EndsWith(".pdf", path);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(FileUtility.CreateTempFile(_folder, (byte[]?)null)));
    }
#pragma warning restore CS0618

    // --- delete ------------------------------------------------------------------------------------------------------

    [Fact]
    public void DeleteFileIfExists_DeletesOrIgnores()
    {
        var path = Path.Combine(_folder, "a.txt");
        File.WriteAllText(path, "x");

        FileUtility.DeleteFileIfExists(path);
        FileUtility.DeleteFileIfExists(path);
        FileUtility.DeleteFileIfExists(null);
        FileUtility.DeleteFileIfExists(Path.Combine(_folder, "never-existed"));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TryDeleteFile_ReportsTheOutcome()
    {
        var path = Path.Combine(_folder, "a.txt");
        File.WriteAllText(path, "x");

        Assert.True(FileUtility.TryDeleteFile(path));
        Assert.True(FileUtility.TryDeleteFile(path)); // already gone
        Assert.True(FileUtility.TryDeleteFile(null));
        Assert.True(FileUtility.TryDeleteFile(string.Empty));
    }

    [Fact]
    public void TryDeleteFile_ReturnsFalseWhenTheFileCannotBeDeleted()
    {
        // a directory path with a file-like name cannot be deleted as a file
        var directory = Path.Combine(_folder, "dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "inner.txt"), "x");
        var locked = Path.Combine(_folder, "locked.txt");
        File.WriteAllText(locked, "x");
        if (OperatingSystem.IsWindows())
        {
            using var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.False(FileUtility.TryDeleteFile(locked));
        }
        else
        {
            // unix allows deleting open files; make the parent read-only instead
            File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                Assert.False(FileUtility.TryDeleteFile(locked));
            }
            finally
            {
                File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    [Fact]
    public async Task DeleteAsync_DeletesWithoutBlocking_MissingFileIsFine()
    {
        var path = Path.Combine(_folder, "a.txt");
        File.WriteAllText(path, "x");

        await FileUtility.DeleteAsync(path);
        await FileUtility.DeleteAsync(path);

        Assert.False(File.Exists(path));
    }

    // --- read / write -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetFileContent_AndAsync_ReadTheWholeFile()
    {
        var path = Path.Combine(_folder, "big.bin");
        var data = new byte[3 * 1024 * 1024 + 17]; // larger than the old single read buffer
        new Random(1).NextBytes(data);
        File.WriteAllBytes(path, data);

        Assert.Equal(data, FileUtility.GetFileContent(path));
        Assert.Equal(data, await FileUtility.GetFileContentAsync(path, Ct)); // one ReadAsync used to return part of the file (D-25)
        await Assert.ThrowsAsync<FileNotFoundException>(() => FileUtility.GetFileContentAsync(Path.Combine(_folder, "missing"), Ct));
    }

    [Fact]
    public void WriteFileContent_Bytes_CreatesDirectoryOnRequest()
    {
        var path = Path.Combine(_folder, "new", "deeper", "f.bin");

        Assert.ThrowsAny<IOException>(() => FileUtility.WriteFileContent(new byte[] { 1 }, path, createDirectory: false));
        FileUtility.WriteFileContent(new byte[] { 1 }, path, createDirectory: true);

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteFileContent_Text_IsUtf8WithByteOrderMark()
    {
        var path = Path.Combine(_folder, "t.txt");

        FileUtility.WriteFileContent("héllo", path, createDirectory: false);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3)); // the BOM is kept for existing readers
        Assert.Equal("héllo", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        Assert.Throws<ArgumentNullException>(() => FileUtility.WriteFileContent((string)null!, path, false));
    }

    [Fact]
    public async Task WriteFileContentAsync_WritesAndCreatesDirectories()
    {
        var path = Path.Combine(_folder, "async", "f.bin");

        await FileUtility.WriteFileContentAsync(new byte[] { 5, 6 }, path, createDirectory: true);

        Assert.Equal(new byte[] { 5, 6 }, await File.ReadAllBytesAsync(path, Ct));
        var bare = "ttg-bare-" + Guid.NewGuid().ToString("N") + ".bin"; // no directory part: createDirectory must not fail
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_folder);
        try
        {
            await FileUtility.WriteFileContentAsync(new byte[] { 1 }, bare, createDirectory: true);
            Assert.True(File.Exists(Path.Combine(_folder, bare)));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    // --- EnsureUniqueFilename -----------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureUniqueFilename_FreeName_IsKept()
    {
        var (success, name, message) = await FileUtility.EnsureUniqueFilename(_folder, "report.pdf");

        Assert.True(success);
        Assert.Equal("report.pdf", name);
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public async Task EnsureUniqueFilename_TakenName_GetsANumber()
    {
        File.WriteAllText(Path.Combine(_folder, "report.pdf"), "x");
        File.WriteAllText(Path.Combine(_folder, "report1.pdf"), "x");

        var (success, name, message) = await FileUtility.EnsureUniqueFilename(_folder, "report.pdf");

        Assert.True(success);
        Assert.Equal("report2.pdf", name);
        Assert.Contains("2 tries", message);
    }

    [Fact]
    public async Task EnsureUniqueFilename_WithALimit_StillFindsAFreeNameWithinIt()
    {
        // with any limit above 0 the loop never ran and every name was reported as not unique (D-24)
        File.WriteAllText(Path.Combine(_folder, "a.txt"), "x");

        var (success, name, _) = await FileUtility.EnsureUniqueFilename(_folder, "a.txt", counterLimit: 5);

        Assert.True(success);
        Assert.Equal("a1.txt", name);
    }

    [Fact]
    public async Task EnsureUniqueFilename_LimitReached_ReportsFailure()
    {
        foreach (var name in new[] { "a.txt", "a1.txt", "a2.txt" })
        {
            File.WriteAllText(Path.Combine(_folder, name), "x");
        }

        var (success, _, message) = await FileUtility.EnsureUniqueFilename(_folder, "a.txt", counterLimit: 2);

        Assert.False(success);
        Assert.Contains("limit of 2", message);
    }

    [Fact]
    public async Task EnsureUniqueFilename_LastAllowedNumberIsChecked()
    {
        File.WriteAllText(Path.Combine(_folder, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_folder, "a1.txt"), "x");

        var (success, name, _) = await FileUtility.EnsureUniqueFilename(_folder, "a.txt", counterLimit: 2);

        Assert.True(success);
        Assert.Equal("a2.txt", name);
    }

    [Fact]
    public async Task EnsureUniqueFilename_InvalidPath_ReportsTheError()
    {
        var (success, _, message) = await FileUtility.EnsureUniqueFilename(null!, "a.txt");

        Assert.False(success);
        Assert.Contains("Couldn't get filename", message);
    }

    [Fact]
    public async Task EnsureUniqueFilename_NoExtension_Works()
    {
        File.WriteAllText(Path.Combine(_folder, "README"), "x");

        var (_, name, _) = await FileUtility.EnsureUniqueFilename(_folder, "README");

        Assert.Equal("README1", name);
    }
}
