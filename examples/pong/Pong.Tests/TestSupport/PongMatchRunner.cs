using Pong.Common;

namespace Pong.Tests.TestSupport;

/// <summary>
/// Plays deterministic points on a <see cref="PongSimulation"/> by driving both paddles.
/// </summary>
/// <remarks>
/// The intended conceding paddle is moved away from the ball while the other paddle returns to the
/// court center. A serve travels toward the side that conceded, so a point normally finishes after a
/// single return; the loop has a generous bound so a regression fails instead of hanging the test.
/// </remarks>
internal static class PongMatchRunner
{
    private const float Step = 1f / 120f;
    private const float DeadZone = 0.125f;
    private const int MaximumStepsPerPoint = 20000;

    /// <summary>Plays until exactly one point is scored by the side opposite <paramref name="conceding"/>.</summary>
    /// <param name="simulation">The simulation to advance; not paused and not finished.</param>
    /// <param name="conceding">The side that should miss the ball.</param>
    /// <exception cref="InvalidOperationException">The point did not finish within the step bound.</exception>
    internal static void PlayPoint(PongSimulation simulation, PongSide conceding)
    {
        int leftBefore = simulation.LeftScore;
        int rightBefore = simulation.RightScore;

        for (int step = 0; step < MaximumStepsPerPoint; step++)
        {
            simulation.Step(
                Step,
                Drive(simulation.LeftPaddleY, LeftTarget(simulation, conceding)),
                Drive(simulation.RightPaddleY, RightTarget(simulation, conceding)));

            if (simulation.LeftScore != leftBefore || simulation.RightScore != rightBefore)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"The point for the conceding side '{conceding}' did not finish within "
                + $"{MaximumStepsPerPoint} steps.");
    }

    private static float LeftTarget(PongSimulation simulation, PongSide conceding)
        => conceding == PongSide.Left ? AwayFromBall(simulation) : 0f;

    private static float RightTarget(PongSimulation simulation, PongSide conceding)
        => conceding == PongSide.Right ? AwayFromBall(simulation) : 0f;

    private static float AwayFromBall(PongSimulation simulation)
        => simulation.BallPosition.Y >= 0f
            ? -simulation.MaxPaddleY
            : simulation.MaxPaddleY;

    private static float Drive(float currentY, float targetY)
    {
        float delta = targetY - currentY;
        return MathF.Abs(delta) <= DeadZone ? 0f : MathF.CopySign(1f, delta);
    }
}
