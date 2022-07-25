using TabTabGo.Core.Enums;

namespace TabTabGo.Core.ViewModels
{
    public class ExportConfiguration
    {
        public ExportType FileType { get; set; } = ExportType.Csv;
        public string FileName { get; set; }
        public IList<ExportColumn> Columns { get; set; }
        public IList<object> FilterKeys { get; set; }

        public string CollectionDelimiter { get; set; } = "; ";
        public string DateFormat { get; set; } = "MM/dd/yyyy";
        public string DateTimeFormat { get; set; } = "MM/dd/yyyy hh:mm:ss";
        [JsonExtensionData]
        public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

    }
}

