using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Json;

/// <summary>
/// Options used when System.Text.Json content is read into Newtonsoft.Json trees.
/// </summary>
public sealed record JsonInteropOptions
{
    /// <summary>Shared default options (floats as <see cref="double"/>, depth 64).</summary>
    public static JsonInteropOptions Default { get; } = new();

    /// <summary>
    /// How floating point numbers are read by Newtonsoft. Use <see cref="Nj.FloatParseHandling.Decimal"/> to keep money exact.
    /// </summary>
    public Nj.FloatParseHandling FloatParseHandling { get; init; } = Nj.FloatParseHandling.Double;

    /// <summary>
    /// Maximum nesting depth accepted when parsing. Dates are always read as strings (<c>DateParseHandling.None</c>).
    /// </summary>
    public int MaxDepth { get; init; } = 64;
}
