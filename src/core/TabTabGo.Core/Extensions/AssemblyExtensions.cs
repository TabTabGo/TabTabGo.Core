using System.IO.Compression;
using System.Reflection;


namespace TabTabGo.Core.Extensions;

public static class AssemblyExtensions
{
    private static object globalObj = new object();

    public static void ExtractResources(this Assembly assembly, string resourcePrefix, string outputPath)
    {
        var resourcesList = assembly.GetManifestResourceNames();

        foreach (var resName in resourcesList)
        {
            if (!resName.StartsWith(resourcePrefix))
                continue;
            var res = resName.Substring(resourcePrefix.Length);
            var resExt = Path.GetExtension(res);


            lock (globalObj)
            {
                // ensure that folder exists
                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                var resStream = assembly.GetManifestResourceStream(resName);

                if (resExt.Equals(".gz", StringComparison.CurrentCultureIgnoreCase))
                {
                    var targetFileName = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(res));

                    if (File.Exists(targetFileName))
                    {
                        if (File.GetLastWriteTime(targetFileName) > File.GetLastWriteTime(assembly.Location))
                            continue;
                    }

                    using (var inputStream = new GZipStream(resStream, CompressionMode.Decompress, false))
                    {
                        using (var outputStream = new FileStream(targetFileName, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buf = new byte[64 * 1024];
                            int read;
                            while ((read = inputStream.Read(buf, 0, buf.Length)) > 0)
                                outputStream.Write(buf, 0, read);
                        }
                    }
                }
                else
                {
                    var targetFileName = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(res) + resExt);

                    if (File.Exists(targetFileName))
                    {
                        if (File.GetLastWriteTime(targetFileName) > File.GetLastWriteTime(assembly.Location))
                            continue;
                    }

                    using (var outputStream = new FileStream(targetFileName, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buf = new byte[64 * 1024];
                        int read;
                        while ((read = resStream.Read(buf, 0, buf.Length)) > 0)
                            outputStream.Write(buf, 0, read);
                    }
                }
            }
        }
    }

}

