using System.Reflection;
using System.Text.Json;

namespace Nma.Lms.Test.Helpers;
public static class FileHelper
{
    private static Object fileLock = new Object();

    public static string GetRootPath()
    {
        var rootPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        return Tools.GetUntilOrEmpty(rootPath, "bin");
    }

    public static async Task AddDataToJsonArrayInFile<T>(string path, string jsonDataToAdd)
    {
        var data = DeserializeObj<T>(jsonDataToAdd);
        List<T> listOfData = new List<T> { data };

        if (IsExist(path))
        {
            lock (fileLock)
            {
                var oldData = FileToObject<List<T>>(path).Result;
                listOfData.AddRange(oldData);
            }
        }
        lock (fileLock)
            File.WriteAllTextAsync(path, SerializeObj<List<T>>(listOfData)).Wait();
    }

    public static async Task UpdateDataToJsonArrayInFile<T>(string path, string jsonData)
    {
        var data = DeserializeObj<List<T>>(jsonData);
        lock (fileLock)
            File.WriteAllTextAsync(path, SerializeObj<List<T>>(data)).Wait();
    }

    public static async Task<string> ReadJsonFile(string path)
    {
        lock (fileLock)
        {
            return File.ReadAllTextAsync(path).Result;
        }
    }

    public static bool IsExist(string path)
        => File.Exists(path);

    public static T DeserializeObj<T>(string jsonData)
        => JsonSerializer.Deserialize<T>(jsonData);

    public static async Task<T> FileToObject<T>(string path)
    {
        lock (fileLock)
            return JsonSerializer.Deserialize<T>(ReadJsonFile(path).Result);
    }

    public static string SerializeObj<T>(T obj)
        => JsonSerializer.Serialize(obj);

    public static bool IsFileReady(string filename)
    {
        // If the file can be opened for exclusive access it means that the file
        // is no longer locked by another process.
        try
        {
            using (FileStream inputStream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None))
                return inputStream.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}