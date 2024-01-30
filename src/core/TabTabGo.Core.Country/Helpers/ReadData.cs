
using System.Reflection;
using System.Text.Json;
using TabTabGo.Core.Models;

namespace Nma.Lms.Test.Helpers;

public static class ReadData
{
    public static async Task<List<Country>> GetCountries(CancellationToken cancellationToken = default)
    {
        var resourcePath = $"{Assembly.Load("TabTabGo.Core.Country").GetName().Name}.Data.countries_info.json";

        using (var stream = Assembly.Load("TabTabGo.Core.Country").GetManifestResourceStream(resourcePath))
        {
            if (stream == null)
            {
                throw new Exception("Resource not found");
            }

            using (var reader = new StreamReader(stream))
            {
                var json = await reader.ReadToEndAsync();
                return JsonSerializer.Deserialize<List<Country>>(json);
            }
        }
    }
}
