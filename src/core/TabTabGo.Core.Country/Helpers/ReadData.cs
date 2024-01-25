
using TabTabGo.Core.Models;

namespace Nma.Lms.Test.Helpers;

public static class ReadData
{
    private const string Path = "Data/";

    public async static Task<List<Country>> GetCountries(CancellationToken cancellationToken = default)
        => await FileHelper.FileToObject<List<Country>>(Path + "countries_info.json");
}
