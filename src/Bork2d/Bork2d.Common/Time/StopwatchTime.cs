using System.Diagnostics;

namespace Bork2d.Common.Time;

/// <summary>
/// A monotonic, <see cref="Stopwatch"/>-based <see cref="ITime"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// The first <see cref="NewFrame"/> publishes <c>Time = 0</c> and <c>DeltaTime = 0</c>; the first
/// tick's timestamp becomes the epoch. Afterwards <see cref="Time"/> is the raw monotonic seconds
/// since that epoch and is never clamped, while <see cref="DeltaTime"/> is the raw interval between
/// tick starts clamped to the builder's <c>MaxDeltaTime</c>. The clamp limits a gameplay step after
/// a stall; it is not a pacing delay.
/// </para>
/// <para>
/// Only the windowed loop calls <see cref="NewFrame"/>, but reads may happen on any thread. A short
/// lock publishes both values together, so readers never observe a torn or half-updated pair.
/// </para>
/// </remarks>
internal sealed class StopwatchTime : IFrameTime
{
    private readonly Lock _gate = new();
    private readonly double _maxDeltaTime;
    private long _firstTick;
    private long _lastTick;
    private bool _started;
    private double _time;
    private double _deltaTime;

    /// <summary>Initializes a new instance of the <see cref="StopwatchTime"/> class.</summary>
    /// <param name="maxDeltaTime">
    /// The positive delta-time clamp in seconds. Positive infinity disables the clamp.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxDeltaTime"/> is zero, negative, or NaN.
    /// </exception>
    internal StopwatchTime(double maxDeltaTime)
    {
        if (!(maxDeltaTime > 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDeltaTime),
                maxDeltaTime,
                "Max delta time must be greater than zero and not NaN.");
        }

        _maxDeltaTime = maxDeltaTime;
    }

    /// <inheritdoc/>
    public double DeltaTime
    {
        get
        {
            lock (_gate)
            {
                return _deltaTime;
            }
        }
    }

    /// <inheritdoc/>
    public double Time
    {
        get
        {
            lock (_gate)
            {
                return _time;
            }
        }
    }

    /// <inheritdoc/>
    public void NewFrame()
    {
        var tick = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (!_started)
            {
                _started = true;
                _firstTick = tick;
                _lastTick = tick;
                _time = 0;
                _deltaTime = 0;
                return;
            }

            // Time is raw monotonic seconds since the first tick and never clamped.
            _time = Stopwatch.GetElapsedTime(_firstTick, tick).TotalSeconds;
            var rawDelta = Stopwatch.GetElapsedTime(_lastTick, tick).TotalSeconds;
            _deltaTime = rawDelta > _maxDeltaTime ? _maxDeltaTime : rawDelta;
            _lastTick = tick;
        }
    }
}
