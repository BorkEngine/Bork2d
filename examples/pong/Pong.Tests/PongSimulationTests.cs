using System.Numerics;
using Pong.Common;
using Pong.Tests.TestSupport;
using Xunit;

namespace Pong.Tests;

/// <summary>
/// Deterministic coverage of the engine-free <see cref="PongSimulation"/>: the serve delay, ball
/// movement and bounces, scoring, match end/reset, pause, resize clamping, and neutral non-finite
/// input.
/// </summary>
public sealed class PongSimulationTests
{
    private const float Step = 1f / 120f;

    [Fact]
    public void OpeningServeWaitsForTheDelayThenLaunchesAndMovesTheBall()
    {
        var simulation = new PongSimulation(PongTestSettings.Create(ballSpeed: 2f), 2f, 1f);

        Assert.True(simulation.IsServing);
        Assert.Equal(Vector2.Zero, simulation.BallPosition);
        Assert.Equal(Vector2.Zero, simulation.BallVelocity);

        simulation.Step(PongSimulation.InitialServeDelay - 0.01f, leftInput: 0f, rightInput: 0f);
        Assert.True(simulation.IsServing);
        Assert.Equal(Vector2.Zero, simulation.BallPosition);

        simulation.Step(0.02f, leftInput: 0f, rightInput: 0f);

        Assert.False(simulation.IsServing);
        Assert.Equal(Vector2.Zero, simulation.BallPosition);
        Vector2 launchedVelocity = simulation.BallVelocity;
        Assert.NotEqual(Vector2.Zero, launchedVelocity);
        Assert.True(launchedVelocity.X < 0f, "The opening serve must travel toward the left side.");

        simulation.Step(1f / 60f, leftInput: 0f, rightInput: 0f);
        Assert.NotEqual(Vector2.Zero, simulation.BallPosition);
        Assert.True(simulation.BallPosition.X < 0f);
    }

    [Fact]
    public void BallBouncesOffTheTopAndBottomWallsInsideTheCourt()
    {
        var simulation = new PongSimulation(PongTestSettings.Create(ballSpeed: 2f), 100f, 1f);
        simulation.Step(PongSimulation.InitialServeDelay, leftInput: 0f, rightInput: 0f);

        float highestY = float.MinValue;
        float lowestY = float.MaxValue;
        var bouncedDownward = false;

        for (var step = 0; step < 300; step++)
        {
            simulation.Step(1f / 60f, leftInput: 0f, rightInput: 0f);
            highestY = MathF.Max(highestY, simulation.BallPosition.Y);
            lowestY = MathF.Min(lowestY, simulation.BallPosition.Y);
            if (simulation.BallVelocity.Y < 0f)
            {
                bouncedDownward = true;
            }
        }

        float limit = 1f - PongSimulation.BallRadius;
        Assert.True(bouncedDownward, "The ball must reverse direction at the top wall.");
        Assert.True(highestY <= limit + 0.0001f, $"The ball reached y={highestY}, above {limit}.");
        Assert.True(lowestY >= -limit - 0.0001f, $"The ball reached y={lowestY}, below {-limit}.");
    }

    [Fact]
    public void PaddleHitReflectsTheBallWithoutScoring()
    {
        var simulation = new PongSimulation(PongTestSettings.Create(ballSpeed: 1f), 2f, 1f);
        simulation.Step(PongSimulation.InitialServeDelay, leftInput: 0f, rightInput: 0f);

        var reflected = false;
        for (var step = 0; step < 600 && !reflected; step++)
        {
            simulation.Step(Step, leftInput: 0f, rightInput: 0f);
            reflected = simulation.BallVelocity.X > 0f;
        }

        Assert.True(reflected, "The ball must reflect off the left paddle.");
        Assert.Equal(0, simulation.LeftScore);
        Assert.Equal(0, simulation.RightScore);
        Assert.InRange(simulation.BallPosition.X, -2f, 2f);
    }

    [Fact]
    public void MissedBallScoresAndServesTowardTheConcedingSide()
    {
        var simulation = new PongSimulation(
            PongTestSettings.Create(paddleSpeed: 10f, ballSpeed: 2f),
            2f,
            4f);

        for (var step = 0; step < 600 && simulation.RightScore == 0; step++)
        {
            simulation.Step(Step, leftInput: 1f, rightInput: 0f);
        }

        Assert.Equal(0, simulation.LeftScore);
        Assert.Equal(1, simulation.RightScore);
        Assert.True(simulation.IsServing);
        Assert.Equal(PongSide.Left, simulation.ServingSide);
        Assert.Equal(Vector2.Zero, simulation.BallPosition);
        Assert.Equal(Vector2.Zero, simulation.BallVelocity);
    }

    [Fact]
    public void WinningScoreEndsTheMatchAndResetMatchClearsTheState()
    {
        var simulation = new PongSimulation(
            PongTestSettings.Create(paddleSpeed: 10f, ballSpeed: 2f, winningScore: 1),
            2f,
            4f);

        for (var step = 0; step < 600 && !simulation.IsMatchOver; step++)
        {
            simulation.Step(Step, leftInput: 1f, rightInput: 0f);
        }

        Assert.True(simulation.IsMatchOver);
        Assert.Equal(PongSide.Right, simulation.Winner);
        Assert.False(simulation.IsServing);
        Assert.Equal(Vector2.Zero, simulation.BallPosition);
        Assert.Equal(Vector2.Zero, simulation.BallVelocity);

        simulation.Pause();
        Assert.False(simulation.IsPaused);

        simulation.ResetMatch();

        Assert.Equal(0, simulation.LeftScore);
        Assert.Equal(0, simulation.RightScore);
        Assert.Equal(PongSide.None, simulation.Winner);
        Assert.False(simulation.IsPaused);
        Assert.True(simulation.IsServing);
        Assert.Equal(Vector2.Zero, simulation.BallPosition);
        Assert.Equal(Vector2.Zero, simulation.BallVelocity);

        var paused = new PongSimulation(PongTestSettings.Create(), 2f, 1f);
        paused.Pause();
        Assert.True(paused.IsPaused);

        paused.ResetMatch();
        Assert.False(paused.IsPaused);
    }

    [Fact]
    public void PauseStopsMovementAndResumeContinues()
    {
        var simulation = new PongSimulation(
            PongTestSettings.Create(paddleSpeed: 10f, ballSpeed: 2f),
            100f,
            4f);
        simulation.Step(PongSimulation.InitialServeDelay, leftInput: 0f, rightInput: 0f);
        simulation.Step(0.1f, leftInput: 0f, rightInput: 0f);
        Vector2 ballBeforePause = simulation.BallPosition;

        simulation.Pause();
        Assert.True(simulation.IsPaused);

        simulation.Step(1f, leftInput: 1f, rightInput: -1f);
        Assert.Equal(ballBeforePause, simulation.BallPosition);
        Assert.Equal(0f, simulation.LeftPaddleY);
        Assert.Equal(0f, simulation.RightPaddleY);

        simulation.TogglePause();
        Assert.False(simulation.IsPaused);

        simulation.Step(0.1f, leftInput: 0f, rightInput: 0f);
        Assert.NotEqual(ballBeforePause, simulation.BallPosition);
    }

    [Fact]
    public void ResizeCourtClampsThePaddlesAndTheBall()
    {
        var simulation = new PongSimulation(
            PongTestSettings.Create(paddleSpeed: 10f, ballSpeed: 2f),
            10f,
            4f);

        for (var step = 0; step < 180; step++)
        {
            simulation.Step(Step, leftInput: 1f, rightInput: 0f);
        }

        Assert.Equal(simulation.MaxPaddleY, simulation.LeftPaddleY);
        Assert.True(simulation.BallPosition.X < -0.75f);

        simulation.ResizeCourt(1.125f, 1f);

        Assert.Equal(1.125f, simulation.HalfWidth);
        Assert.Equal(1f, simulation.HalfHeight);
        Assert.Equal(0.25f, simulation.MaxPaddleY);
        Assert.Equal(0.25f, simulation.LeftPaddleY);
        Assert.Equal(-0.75f, simulation.BallPosition.X);
        Assert.InRange(simulation.BallPosition.Y, -0.625f, 0.625f);
    }

    [Fact]
    public void NonFiniteDeltaAndInputAreIgnored()
    {
        var simulation = new PongSimulation(
            PongTestSettings.Create(paddleSpeed: 10f, ballSpeed: 2f),
            2f,
            1f);

        simulation.Step(float.NaN, leftInput: 0f, rightInput: 0f);
        simulation.Step(0f, leftInput: 0f, rightInput: 0f);
        simulation.Step(-1f, leftInput: 0f, rightInput: 0f);

        simulation.Step(PongSimulation.InitialServeDelay - 0.01f, leftInput: 0f, rightInput: 0f);
        Assert.True(simulation.IsServing);

        simulation.Step(
            0.01f,
            leftInput: float.NaN,
            rightInput: float.PositiveInfinity);

        Assert.False(simulation.IsServing);
        Assert.Equal(0f, simulation.LeftPaddleY);
        Assert.Equal(0f, simulation.RightPaddleY);
        Assert.False(simulation.IsPaused);
        Assert.Equal(PongSide.None, simulation.Winner);
    }
}
