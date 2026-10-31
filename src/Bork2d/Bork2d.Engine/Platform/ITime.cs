namespace Bork2d.Engine.Platform;

/// <summary>
/// The monotonic time and delta snapshot for the current tick.
/// </summary>
/// <remarks>
/// <para>
/// In windowed and offscreen mode the host advances the clock once per tick, before text dispatch
/// and <c>IGame.Tick</c> (D-M6-07). The first tick publishes <see cref="Time"/> and
/// <see cref="DeltaTime"/> as <c>0</c>; afterwards <see cref="Time"/> is the raw monotonic seconds
/// since that first tick and is never clamped, while <see cref="DeltaTime"/> is the raw interval
/// between tick starts clamped to the builder's <c>MaxDeltaTime</c> (default <c>0.25</c> s). The
/// clamp limits one gameplay step after a stall; it is not a pacing delay.
/// </para>
/// <para>
/// In headless mode the clock is fully synthesized (M7 Q12=L2): tick <c>0</c> reports
/// <see cref="Time"/> and <see cref="DeltaTime"/> as <c>0</c>, and tick <c>N &gt;= 1</c> reports
/// <c>N / ticksPerSecond</c> and <c>1 / ticksPerSecond</c>. <c>MaxDeltaTime</c> does not clamp the
/// synthesized step (F7). The pacing grid catches up with system time after a stall, so the
/// synthesized values advance one exact step per tick and a catch-up run performs many ticks.
/// </para>
/// <para>
/// There is no <c>TickCount</c>, fixed timestep, accumulator, or render interpolation, and the clock
/// does not pause on focus loss or while the window is minimized. Reads are thread-safe snapshots
/// and may come from any thread; the last values stay readable after the run.
/// </para>
/// </remarks>
public interface ITime
{
    /// <summary>Gets the seconds since the previous tick start, clamped to the maximum delta.</summary>
    /// <remarks>
    /// Exactly <c>0</c> on the first tick. In windowed/offscreen mode it is the raw interval between
    /// tick starts clamped to the builder's maximum delta; in headless mode it is exactly
    /// <c>1 / ticksPerSecond</c> from the second tick on and is never clamped. The value is a
    /// snapshot for the current tick, so it is stable for the whole <c>Tick</c> callback.
    /// </remarks>
    double DeltaTime { get; }

    /// <summary>Gets the raw monotonic seconds since the first tick.</summary>
    /// <remarks>
    /// <c>0</c> on the first tick. In windowed/offscreen mode it is monotonic wall-clock seconds
    /// since the first tick and is never clamped; in headless mode it is exactly
    /// <c>tickIndex / ticksPerSecond</c>. Use <see cref="DeltaTime"/> for a stall-safe gameplay step.
    /// </remarks>
    double Time { get; }
}
