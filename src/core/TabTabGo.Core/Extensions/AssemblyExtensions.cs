using System.IO.Compression;

namespace TabTabGo.Core.Extensions;

/// <summary>
/// Helpers for assemblies with embedded resources.
/// </summary>
public static class AssemblyExtensions
{
    private static readonly object GlobalLock = new();

    /// <summary>
    /// Extracts the embedded resources whose name starts with a prefix into a folder. Files already there that are newer
    /// than the assembly are left alone; ".gz" resources are decompressed.
    /// </summary>
    /// <param name="assembly">Assembly that holds the resources</param>
    /// <param name="resourcePrefix">Resource name prefix; it is removed from the file names</param>
    /// <param name="outputPath">Folder to extract to; created when missing</param>
    public static void ExtractResources(this Assembly assembly, string resourcePrefix, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourcePrefix);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        var assemblyTime = string.IsNullOrEmpty(assembly.Location) ? DateTime.MinValue : File.GetLastWriteTime(assembly.Location);

        foreach (var resName in assembly.GetManifestResourceNames())
        {
            if (!resName.StartsWith(resourcePrefix, StringComparison.Ordinal))
                continue;
            var res = resName.Substring(resourcePrefix.Length);
            var resExt = Path.GetExtension(res);
            var isGzip = resExt.Equals(".gz", StringComparison.OrdinalIgnoreCase);
            var targetFileName = Path.Combine(outputPath, isGzip ? Path.GetFileNameWithoutExtension(res) : res);

            lock (GlobalLock)
            {
                Directory.CreateDirectory(outputPath);
                if (File.Exists(targetFileName) && File.GetLastWriteTime(targetFileName) > assemblyTime)
                    continue;

                using var resStream = assembly.GetManifestResourceStream(resName);
                if (resStream == null)
                    continue;

                using var source = isGzip ? new GZipStream(resStream, CompressionMode.Decompress, false) : (Stream)resStream;
                using var outputStream = new FileStream(targetFileName, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(outputStream);
            }
        }
    }
}
