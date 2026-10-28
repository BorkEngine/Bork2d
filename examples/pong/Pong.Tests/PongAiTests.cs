using Pong.Common;
using Pong.Tests.TestSupport;
using Xunit;

namespace Pong.Tests;

/// <summary>
/// Coverage of the deterministic right-paddle policy: the dead zone, the correction sign, and ball
/// tracking while the ball approaches.
/// </summary>
public sealed class PongAiTests
{
    [Fact]
    public void SteerReturnsZeroNearTheTarget()
    {
        var simulation = new PongSimulation(PongTestSettings.Create(paddleSpeed: 1f, ballSpeed: 4f), 4f, 4f);

        Assert.Equal(0f, PongAi.Steer(simulation));
    }

    [Fact]
    public void SteerReturnsTheCorrectSignWhenTheRightPaddleIsOffCenter()
    {
        var aboveCenter = new PongSimulation(
            PongTestSettings.Create(paddleSpeed: 4f, ballSpeed: 4f),
            4f,
            4f);
        aboveCenter.Step(0.5f, leftInput: 0f, rightInput: 1f);

        Assert.True(aboveCenter.RightPaddleY > PongAi.DeadZone);
        Assert.Equal(-PongAi.Speed, PongAi.Steer(aboveCenter));

        var belowCenter = new PongSimulation(
            PongTestSettings.Create(paddleSpeed: 4f, ballSpeed: 4f),
            4f,
            4f);
        belowCenter.Step(0.5f, leftInput: 0f, rightInput: -1f);

        Assert.True(belowCenter.RightPaddleY < -PongAi.DeadZone);
        Assert.Equal(PongAi.Speed, PongAi.Steer(belowCenter));
    }

    [Fact]
    public void SteerTracksTheBallWhenItMovesRight()
    {
        var simulation = new PongSimulation(
            PongTestSettings.Create(paddleSpeed: 1f, ballSpeed: 4f),
            4f,
            4f);

        // Raise the left paddle during the serve so the ball bounces off its upper half and heads
        // back to the right with a positive Y.
        simulation.Step(0.5f, leftInput: 1f, rightInput: 0f);

        var movingRight = false;
        for (var step = 0; step < 600 && !movingRight; step++)
        {
            simulation.Step(1f / 120f, leftInput: 0f, rightInput: 0f);
            movingRight = simulation.BallVelocity.X > 0f;
        }

        Assert.True(movingRight, "The ball must bounce off the left paddle and move right.");
        Assert.True(simulation.BallPosition.Y > PongAi.DeadZone);
        Assert.Equal(PongAi.Speed, PongAi.Steer(simulation));
    }
}
