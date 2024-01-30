// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using File = System.IO.File;

namespace TabTabGo.Core.Services;

public class JsonStringLocalizer : IStringLocalizer
{
    private readonly JsonSerializer _serializer = new();
    private readonly IDistributedCache _distributedCache;
    public JsonStringLocalizer(IDistributedCache distributedCache)
    {
        _distributedCache = distributedCache;
    }

    public LocalizedString this[string name]
    {
        get
        {
            var value = GetString(name);
            return new LocalizedString(name, value);

        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = GetString(name, arguments?.FirstOrDefault()?.ToString());

            return new LocalizedString(name, value);
        }
    }
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        throw new NotImplementedException();
    }

    private string GetString(string key, string culture = "en")
    {
        var cultureInfo = GetCultureInfo(culture);

        var cacheKey = $"locale_{culture}_{key}";
        var cacheValue = _distributedCache.GetString(cacheKey);

        if (!string.IsNullOrEmpty(cacheValue))
            return cacheValue;

        var fullFilePath = Path.GetFullPath($"Resources/{cultureInfo.Name}.json");

        if (!File.Exists(fullFilePath))
            return string.Empty;


        var result = GetValueFromJson(key, fullFilePath);

        if (!string.IsNullOrEmpty(result))
            _distributedCache.SetString(cacheKey, result);

        return result;
    }
    private string GetValueFromJson(string key, string filePath)
    {
        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var streamReader = new StreamReader(fileStream, Encoding.UTF8))
        using (var jsonTextReader = new JsonTextReader(streamReader))
        {
            var root = _serializer.Deserialize<JToken>(jsonTextReader);

            var tokens = key.Split('.');
            foreach (var token in tokens)
            {
                root = root[token];
                if (root == null)
                    return string.Empty;
            }
            var result = root.Value<string>();
            return result;
        }
    }
    private CultureInfo GetCultureInfo(string culture = "en")
    {
        switch (culture.ToLower())
        {
            case "en":
                return new CultureInfo("en-US");
            case "ar":
                return new CultureInfo("ar-AE");
            default:
                return CultureInfo.CurrentCulture;
        }
    }
}
