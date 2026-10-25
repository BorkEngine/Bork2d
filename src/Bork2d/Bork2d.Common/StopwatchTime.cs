using System.Diagnostics;
using Bork2d.Api;

namespace Bork2d.Common;

public class StopwatchTime: ITime
{
    public double DeltaTime { get; private set; }
    public double Time { get; private set; }

    private readonly long _startTick;
    private long _lastTick;

    public StopwatchTime()
    {
        _startTick = Stopwatch.GetTimestamp();
        _lastTick = _startTick;
        DeltaTime = 0;
        Time = 0;
    }
    
    public void NewFrame()
    {
        var tick = Stopwatch.GetTimestamp();
        Time = Stopwatch.GetElapsedTime(_startTick, tick).TotalSeconds;
        DeltaTime = Stopwatch.GetElapsedTime(_lastTick, tick).TotalSeconds;
        _lastTick = tick;
    }
}