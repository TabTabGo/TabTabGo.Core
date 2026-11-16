using TabTabGo.Core.Enums;
using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.ViewModels;

/// <summary>
/// Options of an export request.
/// </summary>
public class ExportConfiguration
{
    /// <summary>File type.</summary>
    public ExportType FileType { get; set; } = ExportType.Csv;

    /// <summary>File name (without extension); the entity name is used when empty.</summary>
    public string? FileName { get; set; }

    /// <summary>Columns to export.</summary>
    public IList<ExportColumn> Columns { get; set; } = new List<ExportColumn>();

    /// <summary>Filter values.</summary>
    public IList<object> FilterKeys { get; set; } = new List<object>();

    /// <summary>Delimiter between collection items.</summary>
    public string CollectionDelimiter { get; set; } = "; ";

    /// <summary>Date format.</summary>
    public string DateFormat { get; set; } = "MM/dd/yyyy";

    /// <summary>Date time format.</summary>
    public string DateTimeFormat { get; set; } = "MM/dd/yyyy hh:mm:ss";

    /// <summary>Extra properties flattened into the JSON object.</summary>
    [JsonExtensionData]
    [Nj.JsonExtensionData]
    public IDictionary<string, object?> Properties { get; set; } = new Dictionary<string, object?>();
}
