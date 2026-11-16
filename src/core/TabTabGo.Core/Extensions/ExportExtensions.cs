using TabTabGo.Core.Enums;

namespace TabTabGo.Core.Extensions;

/// <summary>
/// Helpers for <see cref="ExportType"/>.
/// </summary>
public static class ExportExtensions
{
    /// <summary>File extension (with the dot) of an export type; ".csv" for types without a dedicated one.</summary>
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
