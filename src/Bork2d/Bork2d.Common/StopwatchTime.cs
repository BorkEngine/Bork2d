using System.Diagnostics;
using Bork2d.Api;

namespace Bork2d.Common;

/// <summary>
/// A monotonic, <see cref="Stopwatch"/>-based <see cref="ITime"/> implementation.
/// </summary>
internal sealed class StopwatchTime : ITime
{
    private readonly long _startTick;
    private long _lastTick;

    public StopwatchTime()
    {
        _startTick = Stopwatch.GetTimestamp();
        _lastTick = _startTick;
        DeltaTime = 0;
        Time = 0;
    }

    public double DeltaTime { get; private set; }

    public double Time { get; private set; }

    public void NewFrame()
    {
        var tick = Stopwatch.GetTimestamp();
        Time = Stopwatch.GetElapsedTime(_startTick, tick).TotalSeconds;
        DeltaTime = Stopwatch.GetElapsedTime(_lastTick, tick).TotalSeconds;
        _lastTick = tick;
    }
}
