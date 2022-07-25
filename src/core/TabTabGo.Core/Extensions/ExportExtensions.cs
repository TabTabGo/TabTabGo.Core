using TabTabGo.Core.Enums;

namespace TabTabGo.Core.Extensions;

public static class ExportExtensions
{
    public static string FileExtenstion(this ExportType type)
    {
        return type switch
        {
            ExportType.Csv => ".csv",
            ExportType.Html => ".html",
            ExportType.Json => ".json",
            ExportType.Xml => ".xml",
            ExportType.Excel => ".xlsx",
            _ => ".csv"
        };
    }
}