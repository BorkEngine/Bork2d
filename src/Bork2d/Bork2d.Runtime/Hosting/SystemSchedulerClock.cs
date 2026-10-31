using System.Diagnostics;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// The production <see cref="ISchedulerClock"/>: a monotonic <see cref="Stopwatch"/> origin plus a
/// pure <see cref="Thread.Sleep(int)"/> wait (M7 Q13=M4/F1).
/// </summary>
/// <remarks>
/// The origin is captured when the clock is created, so <see cref="NowSeconds"/> is the monotonic
/// seconds since then. The wait rounds up to whole milliseconds and never spins, yields, or polls;
/// a value below one millisecond sleeps one millisecond, and a non-positive value returns
/// immediately.
/// </remarks>
internal sealed class SystemSchedulerClock : ISchedulerClock
{
    private readonly long _origin = Stopwatch.GetTimestamp();

    /// <inheritdoc/>
    public double NowSeconds => Stopwatch.GetElapsedTime(_origin).TotalSeconds;

    /// <inheritdoc/>
    public void Sleep(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        // Thread.Sleep takes whole milliseconds; round up so a positive wait never returns early.
        var milliseconds = (long)Math.Ceiling(duration.TotalMilliseconds);
        if (milliseconds <= 0)
        {
            return;
        }

        Thread.Sleep(milliseconds > int.MaxValue ? int.MaxValue : (int)milliseconds);
    }
}
