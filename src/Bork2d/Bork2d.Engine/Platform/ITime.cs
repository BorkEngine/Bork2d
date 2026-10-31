namespace Bork2d.Engine.Platform;

/// <summary>
/// The monotonic time and delta snapshot for the current tick.
/// </summary>
/// <remarks>
/// <para>
/// In windowed mode the host advances the clock once per tick, before text dispatch and
/// <c>IGame.Tick</c>. The first tick publishes <see cref="Time"/> and <see cref="DeltaTime"/> as
/// <c>0</c>; afterwards <see cref="Time"/> is the raw monotonic seconds since that first tick and is
/// never clamped, while <see cref="DeltaTime"/> is the raw interval between tick starts clamped to
/// the builder's <c>MaxDeltaTime</c> (default <c>0.25</c> s). The clamp limits one gameplay step
/// after a stall; it is not a pacing delay.
/// </para>
/// <para>
/// In windowed and offscreen mode the host advances the clock once per tick and the first tick is
/// <c>0</c> (D-M6-07). In headless mode the app runs one tick and the clock stays <c>0</c>. There is
/// no <c>TickCount</c>, fixed timestep, accumulator, or render interpolation, and the clock does not
/// pause on focus loss or while the window is minimized. Reads are thread-safe snapshots and may come
/// from any thread.
/// </para>
/// </remarks>
public interface ITime
{
    /// <summary>Gets the seconds since the previous tick start, clamped to the maximum delta.</summary>
    /// <remarks>
    /// Exactly <c>0</c> on the first tick and in headless mode. The value is a snapshot for the
    /// current tick, so it is stable for the whole <c>Tick</c> callback.
    /// </remarks>
    double DeltaTime { get; }

    /// <summary>Gets the raw monotonic seconds since the first tick.</summary>
    /// <remarks>
    /// <c>0</c> on the first tick and in headless mode. This value is never clamped; use
    /// <see cref="DeltaTime"/> for a stall-safe gameplay step.
    /// </remarks>
    double Time { get; }
}
