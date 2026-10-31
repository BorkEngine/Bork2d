using Bork2d.Runtime.Hosting;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic <see cref="ISchedulerClock"/> for the headless pacing grid and the windowed
/// minimized tick cap (Q21=U1).
/// </summary>
/// <remarks>
/// <para>
/// Time advances only when a test calls <see cref="Advance"/> or when the host sleeps. A sleep is
/// recorded and then applied, so the pacer's absolute grid is exercised without any real wait; an
/// <see cref="OnSleep"/> hook can block the host inside a sleep so a test can request
/// <c>IEngine.Exit()</c> from another thread while the pacer is waiting.
/// </para>
/// <para>
/// The recorded list and the current time are read from the test thread while the host thread may
/// be advancing them; every access takes the short lock and snapshots are copied.
/// </para>
/// </remarks>
internal sealed class FakeSchedulerClock : ISchedulerClock
{
    private readonly Lock _gate = new();
    private readonly List<TimeSpan> _sleeps = [];
    private double _now;

    /// <inheritdoc/>
    public double NowSeconds
    {
        get
        {
            lock (_gate)
            {
                return _now;
            }
        }
    }

    /// <summary>
    /// Gets or sets a hook invoked inside <see cref="Sleep"/> before the fake time advances.
    /// </summary>
    /// <remarks>
    /// The hook runs without the clock lock, so it may block until the test releases it and may call
    /// into the host (for example <c>IEngine.Exit()</c>).
    /// </remarks>
    internal Action<TimeSpan>? OnSleep { get; set; }

    /// <summary>Gets the number of recorded sleeps.</summary>
    internal int SleepCount
    {
        get
        {
            lock (_gate)
            {
                return _sleeps.Count;
            }
        }
    }

    /// <summary>Gets the total recorded sleep duration in seconds.</summary>
    internal double TotalSleepSeconds
    {
        get
        {
            lock (_gate)
            {
                var total = 0d;
                foreach (TimeSpan sleep in _sleeps)
                {
                    total += sleep.TotalSeconds;
                }

                return total;
            }
        }
    }

    /// <inheritdoc/>
    public void Sleep(TimeSpan duration)
    {
        lock (_gate)
        {
            _sleeps.Add(duration);
        }

        OnSleep?.Invoke(duration);

        if (duration > TimeSpan.Zero)
        {
            Advance(duration.TotalSeconds);
        }
    }

    /// <summary>Advances the fake monotonic time.</summary>
    /// <param name="seconds">The non-negative seconds to add.</param>
    internal void Advance(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                "Fake time may advance only by a non-negative, non-NaN amount.");
        }

        lock (_gate)
        {
            _now += seconds;
        }
    }

    /// <summary>Returns a copy of the recorded sleeps, in call order.</summary>
    /// <returns>The recorded durations.</returns>
    internal TimeSpan[] SleepSnapshot()
    {
        lock (_gate)
        {
            return [.. _sleeps];
        }
    }
}
