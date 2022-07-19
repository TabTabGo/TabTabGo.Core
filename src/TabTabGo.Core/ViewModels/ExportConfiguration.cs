using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace TabTabGo.Core.Services.ViewModels
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

    public class ExportColumn : ExportColumn<Object>
    {

    }

    public class ExportColumn<T> where T : class
    {
        public string Label { get; set; }
        public string Field { get; set; }
        public int Index { get; set; }
        public bool Hide { get; set; }

        [JsonIgnore]
        public Func<object, string> FormatFunc { get; set; }

        [JsonIgnore]
        public Func<T, string> PropertyValue { get; set; }

    }

    public enum ExportType
    {
        Csv = 1,
        Excel = 2
    }
}

