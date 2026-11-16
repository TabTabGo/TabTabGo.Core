namespace TTG.Core;

/// <summary>
/// Legacy location of the stopwatch. Use <see cref="TabTabGo.Core.Utilities.TTGStopWatch"/>.
/// </summary>
[Obsolete("Moved to TabTabGo.Core.Utilities.TTGStopWatch")]
public class TTGStopWatch : TabTabGo.Core.Utilities.TTGStopWatch
{
    /// <inheritdoc />
    public TTGStopWatch(string module, string? formatProvider = null) : base(module, formatProvider)
    {
    }

    /// <inheritdoc />
    public TTGStopWatch(string module, string refNo, string? formatProvider = null) : base(module, refNo, formatProvider)
    {
    }
}
