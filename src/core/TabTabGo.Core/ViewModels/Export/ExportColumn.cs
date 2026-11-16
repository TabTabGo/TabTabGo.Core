namespace TabTabGo.Core.ViewModels;

/// <summary>
/// One column of an export.
/// </summary>
/// <typeparam name="T">Row type</typeparam>
public class ExportColumn<T> where T : class
{
    /// <summary>Column header.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Field name on the row.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Column order.</summary>
    public int Index { get; set; }

    /// <summary>Hide the column.</summary>
    public bool Hide { get; set; }

    /// <summary>Formats a field value.</summary>
    [JsonIgnore]
    public Func<object, string>? FormatFunc { get; set; }

    /// <summary>Reads the display value from a row.</summary>
    [JsonIgnore]
    public Func<T, string>? PropertyValue { get; set; }
}

/// <summary>
/// One column of an export over untyped rows.
/// </summary>
public class ExportColumn : ExportColumn<Object>
{
}
