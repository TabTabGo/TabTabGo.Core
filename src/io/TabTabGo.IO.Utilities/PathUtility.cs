namespace TabTabGo.IO.Utilities;

/// <summary>
/// Path helpers.
/// </summary>
public class PathUtility
{
    /// <summary>
    /// Gets the temp folder: the custom folder (created when missing) or the system temp path.
    /// </summary>
    /// <param name="customTempFolder">Optional custom folder</param>
    public static string GetTempPath(string? customTempFolder = null)
    {
        // ensure that custom temp folder exists
        if (!string.IsNullOrEmpty(customTempFolder) && !Directory.Exists(customTempFolder))
        {
            Directory.CreateDirectory(customTempFolder);
        }

        return customTempFolder ?? Path.GetTempPath();
    }

    /// <summary>
    /// Resolves a path against the application folder; an absolute path is returned unchanged.
    /// </summary>
    /// <param name="path">Relative or absolute path</param>
    public static string ResolvePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.Combine(AppContext.BaseDirectory, path);
    }

    /// <summary>
    /// Makes sure a directory exists.
    /// </summary>
    /// <param name="path">Directory</param>
    /// <param name="message">Reason when it could not be created or reached</param>
    /// <returns>True when the directory exists</returns>
    public static bool EnsureDirectory(string path, out string message)
    {
        message = string.Empty;
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return true;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            message = $"Cannot create or access directory {path} {x.Message}";
            return false;
        }
    }
}
