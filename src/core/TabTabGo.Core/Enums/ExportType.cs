namespace TabTabGo.Core.Enums;

/// <summary>
/// Formats a list can be exported to.
/// </summary>
public enum ExportType
{
    /// <summary>Comma separated values.</summary>
    Csv = 1,

    /// <summary>Excel workbook.</summary>
    Excel = 2,

    /// <summary>XML.</summary>
    Xml = 5,

    /// <summary>JSON.</summary>
    Json = 6,

    /// <summary>YAML.</summary>
    Yaml = 7,

    /// <summary>HTML.</summary>
    Html = 8,

    /// <summary>PDF.</summary>
    Pdf = 9,
}