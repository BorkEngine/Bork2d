namespace Bork2d.Runtime.Hosting;

/// <summary>
/// The internal monotonic clock and wait seam used by the headless pacing grid and the windowed
/// minimized tick cap (M7 WP-4, Q21=U1).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NowSeconds"/> is monotonic: it never goes backwards and is not wall-clock time.
/// <see cref="Sleep"/> is a pure wait for the requested duration (the production implementation
/// calls <see cref="Thread.Sleep(int)"/>, rounded up to whole milliseconds); it never spins,
/// yields, or polls a deadline, and it is not interruptible.
/// </para>
/// <para>
/// Tests replace the production <see cref="SystemSchedulerClock"/> through the internal builder seam
/// so schedule assertions are deterministic. Production code creates exactly one scheduler per app,
/// shared by the headless pacer and the windowed minimized tick cap.
/// </para>
/// </remarks>
internal interface ISchedulerClock
{
    /// <summary>Gets the monotonic seconds elapsed since the clock's origin.</summary>
    double NowSeconds { get; }

    /// <summary>Blocks the calling thread for at least the requested duration.</summary>
    /// <param name="duration">The wait; a zero or negative duration returns immediately.</param>
    void Sleep(TimeSpan duration);
}
