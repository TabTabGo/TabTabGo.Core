using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TabTabGo.Core.Utilities;

/// <summary>
/// Measures the time of named actions and laps and writes them to a logger.
/// </summary>
public class TTGStopWatch : IDisposable
{
    private string? _currentAction;
    private int _copyIndex;
    private bool _disposed;

    /// <summary>Module name written to the log.</summary>
    public string Module { get; set; }

    /// <summary>TimeSpan format used in the log; milliseconds when null.</summary>
    public string? Format { get; set; }

    /// <summary>Reference number written to the log.</summary>
    public string? ReferenceNo { get; set; }

    /// <summary>Logger used by <see cref="CommitLog"/>; nothing is logged when null.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Recorded laps by action name.</summary>
    protected Dictionary<string, TimeSpan> Laps { get; set; } = new();

    /// <summary>Total stopwatch.</summary>
    protected Stopwatch Stopwatch { get; set; }

    /// <summary>Stopwatch of the current lap.</summary>
    protected Stopwatch LapWatch { get; set; }

    /// <summary>True after <see cref="Lap"/> was used since the last start.</summary>
    protected bool IsLapUsed { get; set; }

    /// <summary>Copy of the recorded laps.</summary>
    public IReadOnlyDictionary<string, TimeSpan> LapTimes
    {
        get
        {
            ThrowIfDisposed();
            return new Dictionary<string, TimeSpan>(Laps);
        }
    }

    /// <summary>Creates a stopwatch for a module.</summary>
    public TTGStopWatch(string module, string? formatProvider = null)
    {
        Module = module;
        Stopwatch = new Stopwatch();
        LapWatch = new Stopwatch();
        Format = formatProvider;
    }

    /// <summary>Creates a stopwatch for a module and reference number.</summary>
    public TTGStopWatch(string module, string refNo, string? formatProvider = null) : this(module, formatProvider)
    {
        ReferenceNo = refNo;
    }

    /// <summary>Starts timing an action.</summary>
    public void Start(string action)
    {
        ThrowIfDisposed();
        _currentAction = action;
        Stopwatch.Start();
        LapWatch.Start();
        IsLapUsed = false;
    }

    /// <summary>Records the time since the last lap (or start) under the given name.</summary>
    public void Lap(string action)
    {
        ThrowIfDisposed();
        IsLapUsed = true;
        if (LapWatch.IsRunning)
        {
            AddLap(action, LapWatch.Elapsed);
            LapWatch.Restart();
        }
        else
        {
            Stopwatch.Start();
            LapWatch.Start();
        }
    }

    /// <summary>Stops timing and records the total under the action given to <see cref="Start"/>.</summary>
    public void Stop()
    {
        ThrowIfDisposed();
        if (_currentAction != null)
        {
            AddLap(_currentAction, Stopwatch.Elapsed);
        }

        _currentAction = null;
        Stopwatch.Stop();
        LapWatch.Stop();
        IsLapUsed = false;
    }

    /// <summary>Stops and clears the running state (recorded laps are kept).</summary>
    public void Reset()
    {
        ThrowIfDisposed();
        IsLapUsed = false;
        _currentAction = null;
        Stopwatch.Reset();
        LapWatch.Reset();
    }

    /// <summary>Records the running action, if any, and starts timing a new one.</summary>
    public void Restart(string action)
    {
        ThrowIfDisposed();
        if (!string.IsNullOrEmpty(_currentAction))
        {
            AddLap(_currentAction, Stopwatch.Elapsed);
        }

        Stopwatch.Restart();
        LapWatch.Restart();
        _currentAction = action;
    }

    /// <summary>Stops if running and writes every lap to <see cref="Logger"/>.</summary>
    public void CommitLog(LogLevel logLevel = LogLevel.Information)
    {
        ThrowIfDisposed();
        if (LapWatch.IsRunning || Stopwatch.IsRunning)
        {
            Stop();
        }

        if (Logger == null)
        {
            return;
        }

        foreach (var (action, elapsed) in Laps)
        {
            var timeTaken = Format != null ? elapsed.ToString(Format) : elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            Logger.Log(logLevel, "Time taken to execute module {Module} action {Action} is {TimeTaken} (reference {ReferenceNo})",
                Module, action, timeTaken, ReferenceNo);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stopwatch.Stop();
        LapWatch.Stop();
        Laps.Clear();
        GC.SuppressFinalize(this);
    }

    private void AddLap(string action, TimeSpan value)
    {
        var name = Laps.ContainsKey(action) ? $"{action}_{_copyIndex++}" : action;
        Laps.Add(name, value);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
