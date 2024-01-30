// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Reflection;
using System.Text;
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

        var resourcePath = $"{Assembly.Load("TabTabGo.Core.Country").GetName().Name}.Resources.{cultureInfo.Name}.json";

        var result = GetValueFromJson(key, resourcePath);

        if (!string.IsNullOrEmpty(result))
            _distributedCache.SetString(cacheKey, result);

        return result;
    }
    private string GetValueFromJson(string key, string resourcePath)
    {
        using (var stream = Assembly.Load("TabTabGo.Core.Country").GetManifestResourceStream(resourcePath))
        {
            if (stream == null)
            {
                throw new Exception("Resource not found");
            }

            using (var streamReader = new StreamReader(stream, Encoding.UTF8))
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
