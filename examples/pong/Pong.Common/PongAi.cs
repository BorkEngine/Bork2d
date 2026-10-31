namespace Pong.Common;

/// <summary>
/// The right-paddle policy: a simple deterministic tracker that is intentionally beatable.
/// </summary>
/// <remarks>
/// <para>
/// The paddle tracks the ball's Y while the ball approaches the right side and drifts back to the
/// court center while the ball moves away. A small dead zone prevents per-tick jitter at the target,
/// and the returned strength is <see cref="Speed"/> so the paddle moves slower than the configured
/// maximum.
/// </para>
/// <para>
/// The policy reads only <see cref="PongSimulation"/> state, so it is engine-free and testable.
/// </para>
/// </remarks>
public static class PongAi
{
    /// <summary>The fraction of the configured paddle speed the AI requests.</summary>
    public const float Speed = 0.85f;

    /// <summary>The Y distance under which the AI stops correcting.</summary>
    public const float DeadZone = 0.125f;

    /// <summary>
    /// Computes the right paddle input for the current simulation state.
    /// </summary>
    /// <param name="simulation">The simulation to read; not stored or modified.</param>
    /// <returns>
    /// An input in <c>[-1, 1]</c>: <see cref="Speed"/> toward the ball while it approaches,
    /// <see cref="Speed"/> toward the center while it moves away, or <c>0</c> inside the dead zone.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="simulation"/> is <see langword="null"/>.</exception>
    public static float Steer(PongSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        float target = simulation.BallVelocity.X > 0f ? simulation.BallPosition.Y : 0f;
        float delta = target - simulation.RightPaddleY;
        if (MathF.Abs(delta) <= DeadZone)
        {
            return 0f;
        }

        return MathF.CopySign(Speed, delta);
    }
}
