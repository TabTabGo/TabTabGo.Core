namespace TabTabGo.Core.ViewModels;

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

public class ExportColumn : ExportColumn<Object>
{
}