using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace TabTabGo.Core.Services;

/// <summary>
/// <see cref="IStringLocalizer"/> over JSON resource files. Each culture file is parsed once per process into a frozen
/// dictionary. Keys may be flat (<c>"AE": "..."</c>) or nested (looked up as <c>a.b.c</c>). Lookup order: the culture, its
/// parent cultures, other cultures of the same language (for example "ar" finds <c>ar-AE</c>), then the default culture.
/// A culture name from a caller is never used to build a path: it must look like a culture (<c>xx</c>, <c>xx-YY</c>) and
/// match a resource that exists, and only existing resources are cached, so callers cannot reach other files or grow the cache.
/// </summary>
public partial class JsonStringLocalizer : IStringLocalizer, ICultureStringLocalizer
{
    private const int MaxCultureLength = 35;

    private static readonly ConcurrentDictionary<string, Lazy<FrozenDictionary<string, string>?>> Cache = new();
    private static readonly ConcurrentDictionary<string, Lazy<string[]>> AvailableCache = new();

    private readonly JsonStringLocalizerOptions _options;
    private readonly Assembly? _assembly;

    /// <summary>
    /// Creates a localizer. The distributed cache is accepted for compatibility and no longer used: parsed resources are
    /// kept in process memory.
    /// </summary>
    /// <param name="distributedCache">Ignored</param>
    /// <param name="options">Resource locations; defaults are used when null</param>
    public JsonStringLocalizer(IDistributedCache? distributedCache = null, IOptions<JsonStringLocalizerOptions>? options = null)
        : this(options?.Value ?? new JsonStringLocalizerOptions())
    {
    }

    /// <summary>Creates a localizer with explicit options.</summary>
    /// <param name="options">Resource locations</param>
    public JsonStringLocalizer(JsonStringLocalizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _assembly = options.ResourceAssembly ?? TryLoad(options.ResourceAssemblyName);
    }

    /// <summary>Number of resource files parsed by this process (used by tests).</summary>
    internal static int ParseCount;

    /// <summary>Number of cache entries (used by tests).</summary>
    internal static int CacheCount => Cache.Count;

    /// <summary>Whether a text is acceptable as a culture name: 2-3 letters followed by sub-tags of 2-8 letters or digits.</summary>
    internal static bool IsValidCultureName(string? culture) =>
        !string.IsNullOrEmpty(culture) && culture.Length <= MaxCultureLength && CulturePattern().IsMatch(culture);

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex CulturePattern();

    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            return Create(name, Find(name, CultureInfo.CurrentUICulture.Name), null);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            return Create(name, Find(name, CultureInfo.CurrentUICulture.Name), arguments);
        }
    }

    /// <inheritdoc />
    public string GetString(string name, string culture, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        var localized = Create(name, Find(name, culture), arguments);
        return localized.Value;
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var result = new Dictionary<string, string>();
        var cultures = includeParentCultures
            ? Candidates(CultureInfo.CurrentUICulture.Name)
            : new List<string> { CultureInfo.CurrentUICulture.Name, _options.DefaultCulture };
        // the first culture that has a key wins, so walk in lookup order
        foreach (var culture in cultures)
        {
            var values = Load(culture);
            if (values == null) continue;
            foreach (var (key, value) in values)
            {
                result.TryAdd(key, value);
            }
        }

        return result.Select(pair => new LocalizedString(pair.Key, pair.Value, resourceNotFound: false)).ToList();
    }

    private static LocalizedString Create(string name, string? value, object[]? arguments)
    {
        if (value == null)
        {
            return new LocalizedString(name, name, resourceNotFound: true);
        }

        var text = arguments is { Length: > 0 } ? string.Format(CultureInfo.CurrentCulture, value, arguments) : value;
        return new LocalizedString(name, text, resourceNotFound: false);
    }

    private string? Find(string name, string culture)
    {
        foreach (var candidate in Candidates(culture))
        {
            var values = Load(candidate);
            if (values != null && values.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>The culture, its parents, other cultures of the same language, then the default culture. An invalid name gives only the default.</summary>
    private List<string> Candidates(string? culture)
    {
        var candidates = new List<string>();
        if (IsValidCultureName(culture))
        {
            var current = culture!;
            while (current.Length > 0)
            {
                candidates.Add(current);
                var dash = current.LastIndexOf('-');
                current = dash > 0 ? current[..dash] : string.Empty;
            }

            var language = candidates[^1];
            candidates.AddRange(AvailableCultures()
                .Where(c => c.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase) && !candidates.Contains(c, StringComparer.OrdinalIgnoreCase))
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
        }

        if (!candidates.Contains(_options.DefaultCulture, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(_options.DefaultCulture);
        }

        return candidates;
    }

    /// <summary>Names of the resources that exist (embedded and files), read once per resource location.</summary>
    private string[] AvailableCultures()
    {
        var key = $"{_assembly?.GetName().FullName}|{_options.ResourcesPath}";
        return AvailableCache.GetOrAdd(key, _ => new Lazy<string[]>(ListAvailableCultures)).Value;
    }

    private string[] ListAvailableCultures()
    {
        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_assembly != null)
        {
            var prefix = EmbeddedPrefix();
            foreach (var resource in _assembly.GetManifestResourceNames())
            {
                if (resource.StartsWith(prefix, StringComparison.Ordinal) && resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var name = resource[prefix.Length..^5];
                    if (IsValidCultureName(name))
                    {
                        cultures.Add(name);
                    }
                }
            }
        }

        if (Directory.Exists(_options.ResourcesPath))
        {
            foreach (var file in Directory.EnumerateFiles(_options.ResourcesPath, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (IsValidCultureName(name))
                {
                    cultures.Add(name);
                }
            }
        }

        return cultures.ToArray();
    }

    private string EmbeddedPrefix() => $"{_assembly!.GetName().Name}.Resources.";

    private FrozenDictionary<string, string>? Load(string? culture)
    {
        if (!IsValidCultureName(culture))
        {
            return null;
        }

        // only a name that matches an existing resource gets a cache entry, spelled the way the resource is named
        var actual = AvailableCultures().FirstOrDefault(c => string.Equals(c, culture, StringComparison.OrdinalIgnoreCase));
        if (actual == null)
        {
            return null;
        }

        var cacheKey = $"{_assembly?.GetName().FullName}|{_options.ResourcesPath}|{actual}";
        return Cache.GetOrAdd(cacheKey, _ => new Lazy<FrozenDictionary<string, string>?>(() => Parse(actual))).Value;
    }

    private FrozenDictionary<string, string>? Parse(string culture)
    {
        var values = new Dictionary<string, string>();
        var found = false;

        if (_assembly != null)
        {
            var resourceName = _assembly.GetManifestResourceNames()
                .FirstOrDefault(r => string.Equals(r, $"{EmbeddedPrefix()}{culture}.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var stream = _assembly.GetManifestResourceStream(resourceName)!;
                using var document = JsonDocument.Parse(stream);
                Flatten(string.Empty, document.RootElement, values);
                found = true;
            }
        }

        var filePath = SafeFilePath(culture);
        if (filePath != null && File.Exists(filePath))
        {
            // file entries override embedded ones
            using var stream = File.OpenRead(filePath);
            using var document = JsonDocument.Parse(stream);
            Flatten(string.Empty, document.RootElement, values);
            found = true;
        }

        if (!found)
        {
            return null;
        }

        Interlocked.Increment(ref ParseCount);
        return values.ToFrozenDictionary();
    }

    /// <summary>The resource file path, or null when it would fall outside the resources folder.</summary>
    private string? SafeFilePath(string culture)
    {
        var root = Path.GetFullPath(_options.ResourcesPath);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, culture + ".json"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(rootWithSeparator, comparison) ? full : null;
    }

    private static void Flatten(string path, JsonElement element, Dictionary<string, string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(path.Length == 0 ? property.Name : $"{path}.{property.Name}", property.Value, values);
                }

                break;
            case JsonValueKind.String:
                values[path] = element.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                break;
            default:
                values[path] = element.ToString();
                break;
        }
    }

    private static Assembly? TryLoad(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }
}
