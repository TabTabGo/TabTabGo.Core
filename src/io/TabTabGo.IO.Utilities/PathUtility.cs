namespace TabTabGo.IO.Utilities;

public class PathUtility
{
    public static string GetTempPath(string customTempFolder = null)
    {
        // ensure that custom temp folder exists
        if (!String.IsNullOrEmpty(customTempFolder) && !Directory.Exists(customTempFolder))
        {
            Directory.CreateDirectory(customTempFolder);
        }

        return customTempFolder ?? Path.GetTempPath();
    }

    public static string ResolvePath(string path)
    {

        var resolvedPath = System.IO.Path.Combine(AppContext.BaseDirectory, path);

        if (resolvedPath == null)
        {
            resolvedPath = Path.GetFullPath(path);
        }

        //if (HttpContext.Current != null)
        //{
        //    return HostingEnvironment.MapPath(path);
        //}
        //else
        //{
        //    return Path.GetFullPath(path);
        //}

        return resolvedPath;
    }

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
        catch (Exception x)
        {
            message = $@"Cannot create or access directory {path} {x.Message}";
            return false;
        }
    }
}