using System.Text;

namespace TabTabGo.IO.Utilities;

/// <summary>
/// File helpers: temp files, safe delete, read and write, unique file names.
/// </summary>
public class FileUtility
{
    /// <summary>
    /// Builds a random file path ("ttg-" prefix) in a folder. The file is not created.
    /// </summary>
    /// <param name="tempPath">Folder</param>
    /// <param name="extension">Extension with or without the leading dot</param>
    public static string GetTempFile(string tempPath, string extension)
    {
        ArgumentNullException.ThrowIfNull(tempPath);
        ArgumentNullException.ThrowIfNull(extension);
        var fileName = "ttg-" + Path.GetRandomFileName();
        if (extension.StartsWith('.'))
        {
            return Path.Combine(tempPath, fileName + extension);
        }

        return Path.Combine(tempPath, fileName + "." + extension);
    }

    /// <summary>
    /// Creates a temp file with the content (an empty path is returned unwritten when content is null).
    /// </summary>
    /// <param name="tempPath">Folder</param>
    /// <param name="extension">Extension</param>
    /// <param name="content">File content; the file is not created when null</param>
    /// <returns>The file path</returns>
    public static string CreateTempFile(string tempPath, string extension, byte[]? content)
    {
        var tmpFilePath = GetTempFile(tempPath, extension);

        if (content != null)
        {
            File.WriteAllBytes(tmpFilePath, content);
        }

        return tmpFilePath;
    }

    /// <summary>
    /// Creates a temp file with a pdf extension.
    /// </summary>
    /// <param name="tempPath">Folder</param>
    /// <param name="content">File content</param>
    [Obsolete("This method generates a temp file with pdf as an extension. Use the CreateTempFile override which accepts a file extension instead.")]
    public static string CreateTempFile(string tempPath, byte[]? content)
    {
        return CreateTempFile(tempPath, "pdf", content);
    }

    /// <summary>
    /// Deletes a file when it exists; failures are ignored. Use <see cref="TryDeleteFile"/> to learn the outcome.
    /// </summary>
    /// <param name="filePath">File path</param>
    public static void DeleteFileIfExists(string? filePath)
    {
        TryDeleteFile(filePath);
    }

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <returns>True when the file is gone (deleted, or it did not exist); false when it could not be deleted</returns>
    public static bool TryDeleteFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return true;
        }

        try
        {
            File.Delete(filePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads a whole file.</summary>
    /// <param name="filePath">File path</param>
    public static byte[] GetFileContent(string filePath)
    {
        return File.ReadAllBytes(filePath);
    }

    /// <summary>Reads a whole file asynchronously.</summary>
    /// <param name="filePath">File path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static Task<byte[]> GetFileContentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return File.ReadAllBytesAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Writes text as UTF-8. The bytes start with the UTF-8 byte order mark (kept for compatibility with existing readers).
    /// </summary>
    /// <param name="fileContent">Text</param>
    /// <param name="filePath">File path</param>
    /// <param name="createDirectory">Create the folder first</param>
    public static void WriteFileContent(string fileContent, string filePath, bool createDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileContent);
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(fileContent);
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        WriteFileContent(bytes, filePath, createDirectory);
    }

    /// <summary>Writes bytes to a file.</summary>
    /// <param name="fileContent">Bytes</param>
    /// <param name="filePath">File path</param>
    /// <param name="createDirectory">Create the folder first</param>
    public static void WriteFileContent(byte[] fileContent, string filePath, bool createDirectory)
    {
        if (createDirectory)
        {
            CreateParentDirectory(filePath);
        }

        File.WriteAllBytes(filePath, fileContent);
    }

    /// <summary>Writes bytes to a file asynchronously.</summary>
    /// <param name="fileContent">Bytes</param>
    /// <param name="filePath">File path</param>
    /// <param name="createDirectory">Create the folder first</param>
    public static async Task WriteFileContentAsync(byte[] fileContent, string filePath, bool createDirectory)
    {
        if (createDirectory)
        {
            CreateParentDirectory(filePath);
        }

        await File.WriteAllBytesAsync(filePath, fileContent);
    }

    private static void CreateParentDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Deletes a file when it exists, without blocking the caller.</summary>
    /// <param name="filePath">File path</param>
    public static Task DeleteAsync(string filePath)
    {
        return Task.Run(() =>
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        });
    }

    /// <summary>
    /// Determines that the filename will be unique (will not exist already) in the given path when created.
    /// If a file already exists with the same name, it keeps adding an integer to the name
    /// to make it unique, until either the filename is unique or counterLimit is reached.
    /// The recommended unique filename is returned in the second component of the Tuple.
    /// If it could not make a unique filename before the counterLimit is reached, or encounters another error,
    /// it returns false in the first component and a message explaining the situation in the 3rd component.
    /// </summary>
    /// <param name="path">Folder</param>
    /// <param name="filename">Wanted file name</param>
    /// <param name="counterLimit">Maximum number of numbered names to try; 0 means no limit</param>
    /// <returns>Tuple&lt;success,newfilename,message&gt;</returns>
    public static Task<Tuple<bool, string, string>> EnsureUniqueFilename(string path, string filename, int counterLimit = 0)
    {
        var foundUnique = false;
        var counter = 0;
        var currentFilename = filename;
        var msg = string.Empty;
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
        var extension = Path.GetExtension(filename);
        try
        {
            while (!foundUnique && (counterLimit == 0 || counter < counterLimit))
            {
                if (!File.Exists(Path.Combine(path, currentFilename)))
                {
                    foundUnique = true;
                }
                else
                {
                    if (counter == int.MaxValue)
                    {
                        msg = $"Couldn't get filename {filename} to be unique, max integer limit reached ({int.MaxValue})";
                        break;
                    }

                    counter++;
                    currentFilename = $"{nameWithoutExtension}{counter}{extension}";
                }
            }

            // the last numbered name is only checked when the loop ended because of the limit
            if (!foundUnique && counterLimit > 0 && counter >= counterLimit && !File.Exists(Path.Combine(path, currentFilename)))
            {
                foundUnique = true;
            }
        }
        catch (Exception x) when (x is IOException or ArgumentException or UnauthorizedAccessException)
        {
            msg = $"Couldn't get filename {filename} to be unique, {x.Message}";
        }

        if (foundUnique && counter > 0)
        {
            msg = $"Unique filename {currentFilename} was reached after {counter} tries.";
        }
        else if (!foundUnique && msg.Length == 0)
        {
            msg = $"Couldn't get filename {filename} to be unique, the limit of {counterLimit} was reached.";
        }

        return Task.FromResult(new Tuple<bool, string, string>(foundUnique, currentFilename, msg));
    }
}
