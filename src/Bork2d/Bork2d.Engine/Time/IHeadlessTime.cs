using Bork2d.Engine.Platform;

namespace Bork2d.Engine.Time;

/// <summary>
/// The internal headless clock the host advances once per tick (M7 WP-4, Q12=L2/F5).
/// </summary>
/// <remarks>
/// <see cref="ITime"/> stays the public surface; the host owns the concrete implementation and calls
/// <see cref="SetTick"/> on the tick thread before the game's <c>Tick</c>. The values are fully
/// synthesized: no real clock is read, so the same tick index always produces the same
/// <see cref="ITime.Time"/> and <see cref="ITime.DeltaTime"/> for the same rate.
/// </remarks>
internal interface IHeadlessTime : ITime
{
    /// <summary>Advances the synthesized clock to the given zero-based tick.</summary>
    /// <param name="tickIndex">The zero-based tick index; <c>0</c> is the first tick.</param>
    /// <param name="ticksPerSecond">The positive configured headless rate.</param>
    /// <remarks>
    /// Tick <c>0</c> publishes <c>Time = DeltaTime = 0</c>; tick <c>N &gt;= 1</c> publishes
    /// <c>Time = N / ticksPerSecond</c> and <c>DeltaTime = 1 / ticksPerSecond</c>. The builder's
    /// max-delta clamp does not apply (F7).
    /// </remarks>
    void SetTick(long tickIndex, int ticksPerSecond);
}
