namespace Bork2d.Engine.Time;

/// <summary>
/// The fully synthesized headless <see cref="IHeadlessTime"/> (M7 WP-4, Q12=L2).
/// </summary>
/// <remarks>
/// <para>
/// The clock never reads the system clock: the host sets the zero-based tick index and the
/// configured rate before each <c>IGame.Tick</c>, and the published values are
/// <c>Time = tickIndex / ticksPerSecond</c> (tick <c>0</c> reports <c>0</c>) and
/// <c>DeltaTime = 1 / ticksPerSecond</c> (tick <c>0</c> reports <c>0</c>). The builder's
/// <c>WithMaxDeltaTime</c> clamp does not apply in headless mode (F7).
/// </para>
/// <para>
/// Reads may happen on any thread; a short lock publishes both values together so a reader never
/// observes a torn or half-updated pair, and the last values stay readable after shutdown
/// (D-M2-17).
/// </para>
/// </remarks>
internal sealed class SynthesizedTime : IHeadlessTime
{
    private readonly Lock _gate = new();
    private double _time;
    private double _deltaTime;

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
    public void SetTick(long tickIndex, int ticksPerSecond)
    {
        if (tickIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tickIndex),
                tickIndex,
                "The tick index must be zero or greater.");
        }

        if (ticksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticksPerSecond),
                ticksPerSecond,
                "The headless ticks-per-second rate must be greater than zero.");
        }

        // The first tick is not one synthesized step (F5); every later tick advances one exact step.
        double time = tickIndex == 0 ? 0d : tickIndex / (double)ticksPerSecond;
        double deltaTime = tickIndex == 0 ? 0d : 1d / ticksPerSecond;

        lock (_gate)
        {
            _time = time;
            _deltaTime = deltaTime;
        }
    }
}
