using System.Reflection;
namespace TabTabGo.Core.Services;

/// <summary>
/// Where <see cref="JsonStringLocalizer"/> finds its resource files (<c>{culture}.json</c>).
/// Entries found in the file directory override the embedded ones.
/// </summary>
public class JsonStringLocalizerOptions
{
    /// <summary>
    /// Assembly that holds embedded resources named <c>{AssemblyName}.Resources.{culture}.json</c>.
    /// When null the assembly named <see cref="ResourceAssemblyName"/> is loaded if it is available.
    /// </summary>
    public Assembly? ResourceAssembly { get; set; }

    /// <summary>Name of the assembly to load when <see cref="ResourceAssembly"/> is null.</summary>
    public string ResourceAssemblyName { get; set; } = "TabTabGo.Core.Country";

    /// <summary>
    /// Folder with <c>{culture}.json</c> files that override the embedded entries.
    /// Defaults to <c>Resources</c> next to the application (never the process working directory).
    /// </summary>
    public string ResourcesPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "Resources");

    /// <summary>Culture used when the requested culture (and its parents) has no entry.</summary>
    public string DefaultCulture { get; set; } = "en";
}
