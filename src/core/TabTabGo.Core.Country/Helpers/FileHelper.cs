using System.Text.Json;

namespace Nma.Lms.Test.Helpers;

/// <summary>
/// Legacy JSON file helper. Removed in 11.x. Relative paths are resolved against the application folder.
/// </summary>
[Obsolete("Removed in 11.x.")]
public static class FileHelper
{
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    /// <summary>The application folder up to "bin".</summary>
    public static string GetRootPath()
    {
        return Tools.GetUntilOrEmpty(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "bin");
    }

    /// <summary>Adds one item to the JSON array stored in a file (the new item first).</summary>
    public static async Task AddDataToJsonArrayInFile<T>(string path, string jsonDataToAdd)
    {
        var data = DeserializeObj<T>(jsonDataToAdd);
        var listOfData = new List<T> { data };
        var fullPath = Resolve(path);

        await FileLock.WaitAsync();
        try
        {
            if (File.Exists(fullPath))
            {
                var oldData = JsonSerializer.Deserialize<List<T>>(await File.ReadAllTextAsync(fullPath));
                if (oldData != null)
                {
                    listOfData.AddRange(oldData);
                }
            }

            await File.WriteAllTextAsync(fullPath, SerializeObj(listOfData));
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <summary>Replaces the JSON array stored in a file.</summary>
    public static async Task UpdateDataToJsonArrayInFile<T>(string path, string jsonData)
    {
        var data = DeserializeObj<List<T>>(jsonData);
        await FileLock.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(Resolve(path), SerializeObj(data));
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <summary>Reads a text file.</summary>
    public static async Task<string> ReadJsonFile(string path)
    {
        await FileLock.WaitAsync();
        try
        {
            return await File.ReadAllTextAsync(Resolve(path));
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <summary>Whether a file exists.</summary>
    public static bool IsExist(string path)
        => File.Exists(Resolve(path));

    /// <summary>Deserializes JSON text.</summary>
    public static T DeserializeObj<T>(string jsonData)
        => JsonSerializer.Deserialize<T>(jsonData) ?? throw new JsonException("The JSON is null.");

    /// <summary>Reads a JSON file into an object.</summary>
    public static async Task<T> FileToObject<T>(string path)
        => DeserializeObj<T>(await ReadJsonFile(path));

    /// <summary>Serializes an object.</summary>
    public static string SerializeObj<T>(T obj)
        => JsonSerializer.Serialize(obj);

    /// <summary>Whether a file can be opened exclusively and is not empty.</summary>
    public static bool IsFileReady(string filename)
    {
        // If the file can be opened for exclusive access it means that the file
        // is no longer locked by another process.
        try
        {
            using var inputStream = File.Open(Resolve(filename), FileMode.Open, FileAccess.Read, FileShare.None);
            return inputStream.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
