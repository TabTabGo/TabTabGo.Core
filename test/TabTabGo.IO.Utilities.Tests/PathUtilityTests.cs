using TabTabGo.IO.Utilities;

namespace TabTabGo.IO.Utilities.Tests;

public sealed class PathUtilityTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ttg-path-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public void GetTempPath_DefaultIsTheSystemTempPath()
    {
        Assert.Equal(Path.GetTempPath(), PathUtility.GetTempPath());
        Assert.Equal(Path.GetTempPath(), PathUtility.GetTempPath(null));
    }

    [Fact]
    public void GetTempPath_CustomFolderIsCreated()
    {
        var result = PathUtility.GetTempPath(_folder);

        Assert.Equal(_folder, result);
        Assert.True(Directory.Exists(_folder));
        Assert.Equal(_folder, PathUtility.GetTempPath(_folder)); // second call, already there
    }

    [Fact]
    public void GetTempPath_EmptyCustomFolder_IsReturnedAsGiven()
    {
        Assert.Equal(string.Empty, PathUtility.GetTempPath(string.Empty));
    }

    [Fact]
    public void ResolvePath_RelativeUsesTheApplicationFolder_AbsoluteIsUntouched()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data", "x.json"), PathUtility.ResolvePath(Path.Combine("data", "x.json")));
        Assert.Equal(_folder, PathUtility.ResolvePath(_folder));
        Assert.Throws<ArgumentNullException>(() => PathUtility.ResolvePath(null!));
    }

    [Fact]
    public void EnsureDirectory_CreatesMissingDirectories()
    {
        var nested = Path.Combine(_folder, "a", "b");

        Assert.True(PathUtility.EnsureDirectory(nested, out var message));

        Assert.True(Directory.Exists(nested));
        Assert.Equal(string.Empty, message);
        Assert.True(PathUtility.EnsureDirectory(nested, out _)); // already exists
    }

    [Fact]
    public void EnsureDirectory_Failure_ReturnsFalseWithAMessage()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "file.txt");
        File.WriteAllText(file, "x");

        var ok = PathUtility.EnsureDirectory(Path.Combine(file, "sub"), out var message); // a file is in the way

        Assert.False(ok);
        Assert.Contains("Cannot create or access directory", message);
        Assert.False(PathUtility.EnsureDirectory("bad\0path", out var nullMessage));
        Assert.NotEmpty(nullMessage);
    }
}
