using System.Reflection;
﻿using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace TabTabGo.Core.Services;

/// <summary>
/// <see cref="IStringLocalizer"/> over JSON resource files. Each culture file is parsed once per process into a frozen
/// dictionary. Keys may be flat (<c>"AE": "..."</c>) or nested (looked up as <c>a.b.c</c>). Lookup order: the culture, its
/// parent cultures, other cultures of the same language (for example "ar" finds <c>ar-AE</c>), then the default culture.
/// </summary>
public class JsonStringLocalizer : IStringLocalizer, ICultureStringLocalizer
{
    private static readonly ConcurrentDictionary<string, Lazy<FrozenDictionary<string, string>?>> Cache = new();

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

    /// <summary>The culture, its parents, other cultures of the same language, then the default culture.</summary>
    private List<string> Candidates(string culture)
    {
        var candidates = new List<string>();
        var current = culture ?? string.Empty;
        while (current.Length > 0)
        {
            candidates.Add(current);
            var dash = current.LastIndexOf('-');
            current = dash > 0 ? current[..dash] : string.Empty;
        }

        if (candidates.Count > 0)
        {
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

    private IEnumerable<string> AvailableCultures()
    {
        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_assembly != null)
        {
            var prefix = EmbeddedPrefix();
            foreach (var resource in _assembly.GetManifestResourceNames())
            {
                if (resource.StartsWith(prefix, StringComparison.Ordinal) && resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    cultures.Add(resource[prefix.Length..^5]);
                }
            }
        }

        if (Directory.Exists(_options.ResourcesPath))
        {
            foreach (var file in Directory.EnumerateFiles(_options.ResourcesPath, "*.json"))
            {
                cultures.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        return cultures;
    }

    private string EmbeddedPrefix() => $"{_assembly!.GetName().Name}.Resources.";

    private FrozenDictionary<string, string>? Load(string culture)
    {
        var cacheKey = $"{_assembly?.GetName().FullName}|{_options.ResourcesPath}|{culture.ToLowerInvariant()}";
        return Cache.GetOrAdd(cacheKey, _ => new Lazy<FrozenDictionary<string, string>?>(() => Parse(culture))).Value;
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

        var filePath = Path.Combine(_options.ResourcesPath, culture + ".json");
        if (File.Exists(filePath))
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
