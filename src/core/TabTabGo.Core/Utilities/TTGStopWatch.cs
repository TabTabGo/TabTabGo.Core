using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TTG.Core;

public class TTGStopWatch : IDisposable
{
    private string _currentAction = null;
    private int _copyIndex = 0;
    public string Module { get; set; }
    public string Format { get; set; }
    protected Dictionary<string, TimeSpan> Laps { get; set; } = new Dictionary<string, TimeSpan>();
    public string ReferenceNo { get; set; }
    protected Stopwatch Stopwatch { get; set; }
    protected Stopwatch LapWatch { get; set; }
    protected bool IsLapUsed { get; set; } = false;

    private readonly ILogger _logger;

    public TTGStopWatch(string module, string formatProvider = null)
    {
        this.Module = module;
        this.Stopwatch = new Stopwatch();
        this.LapWatch = new Stopwatch();
        this.Format = formatProvider;

    }

    public TTGStopWatch(string module, string refNo, string formatProvider = null) : this(module, formatProvider)
    {
        this.ReferenceNo = refNo;
    }

    public void Start(string action)
    {
        _currentAction = action;
        Stopwatch.Start();
        LapWatch.Start();
        IsLapUsed = false;
    }

    public void Lap(string action)
    {
        IsLapUsed = true;
        // _currentAction = action;
        if (LapWatch.IsRunning)
        {
            AddLap(action, LapWatch.Elapsed);
            LapWatch.Restart();
        }
        else
        {
            Stopwatch.Start();
        }
    }

    public void Stop()
    {
        AddLap(_currentAction, Stopwatch.Elapsed);
        _currentAction = null;
        Stopwatch.Stop();
        LapWatch.Stop();
        IsLapUsed = false;
    }

    public void Reset()
    {
        IsLapUsed = false;
        _currentAction = null;
        Stopwatch.Reset();
        LapWatch.Reset();
    }

    public void Restart(string action)
    {
        if (!string.IsNullOrEmpty(_currentAction))
        {
            AddLap(_currentAction, Stopwatch.Elapsed);
        }
        Stopwatch.Restart();
        LapWatch.Restart();
        _currentAction = action;
    }

    public void CommitLog(LogLevel logLevel = LogLevel.Information)
    {
        if (LapWatch.IsRunning || Stopwatch.IsRunning)
        {
            Stop();
        }

        foreach (var timeSpan in Laps)
        {
            var timeTaken = this.Format != null
                ? timeSpan.Value.ToString(this.Format)
                : timeSpan.Value.TotalMilliseconds.ToString();
            // _logger.Log(logLevel, "TTGStopWatch", $"Time taken to execute module: {this.Module} action :{timeSpan.Key} is {timeTaken}", extraData: new
            // {
            //     Module = this.Module,
            //     Action = timeSpan.Key,
            //     TimeTaken = timeTaken,
            //     ReferenceNo = ReferenceNo
            // });
        }

    }

    public void Dispose()
    {
        if (LapWatch != null)
        {
            LapWatch = null;
        }
        if (Stopwatch != null)
        {
            Stopwatch = null;
        }
        if (this.Laps == null) return;
        Laps.Clear();
        Laps = null;
    }

    private void AddLap(string key, TimeSpan value)
    {
        Laps.Add(Laps.ContainsKey(key) ? $"{key}_{_copyIndex++}" : key, value);
    }
}

