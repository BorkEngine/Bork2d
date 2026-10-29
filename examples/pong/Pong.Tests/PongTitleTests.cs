using Pong.Common;
using Pong.Tests.TestSupport;
using Xunit;

namespace Pong.Tests;

/// <summary>
/// Exact window-title text for every match state, including a full 11:7 left win and a 7:11 right
/// win played deterministically through the simulation.
/// </summary>
public sealed class PongTitleTests
{
    [Fact]
    public void ServingTitle()
    {
        var simulation = CreateSimulation();

        Assert.True(simulation.IsServing);
        Assert.Equal("Pong - 0 : 0 - Serving...", PongTitle.Format(simulation));
    }

    [Fact]
    public void RunningTitle()
    {
        var simulation = CreateSimulation();
        Launch(simulation);

        Assert.False(simulation.IsServing);
        Assert.Equal(
            "Pong - 0 : 0 - W/S move, Space pause, R reset, Esc quit",
            PongTitle.Format(simulation));
    }

    [Fact]
    public void PausedTitle()
    {
        var simulation = CreateSimulation();
        Launch(simulation);
        simulation.Pause();

        Assert.True(simulation.IsPaused);
        Assert.Equal("Pong - 0 : 0 - Paused - Space to resume", PongTitle.Format(simulation));
    }

    [Fact]
    public void LeftWinTitle()
    {
        var simulation = CreateSimulation();

        PlayPoints(simulation, PongSide.Left, 7);
        PlayPoints(simulation, PongSide.Right, 1);
        PlayPoints(simulation, PongSide.Right, 10);

        Assert.Equal(11, simulation.LeftScore);
        Assert.Equal(7, simulation.RightScore);
        Assert.Equal(PongSide.Left, simulation.Winner);
        Assert.Equal(
            "Pong - 11 : 7 - Left wins - R to restart, Esc to quit",
            PongTitle.Format(simulation));
    }

    [Fact]
    public void RightWinTitle()
    {
        var simulation = CreateSimulation();

        PlayPoints(simulation, PongSide.Left, 7);
        PlayPoints(simulation, PongSide.Right, 1);
        PlayPoints(simulation, PongSide.Right, 6);
        PlayPoints(simulation, PongSide.Left, 1);
        PlayPoints(simulation, PongSide.Left, 3);

        Assert.Equal(7, simulation.LeftScore);
        Assert.Equal(11, simulation.RightScore);
        Assert.Equal(PongSide.Right, simulation.Winner);
        Assert.Equal(
            "Pong - 7 : 11 - Right wins - R to restart, Esc to quit",
            PongTitle.Format(simulation));
    }

    private static PongSimulation CreateSimulation()
        => new(
            PongTestSettings.Create(paddleSpeed: 14f, ballSpeed: 12f, winningScore: 11),
            2f,
            4f);

    private static void Launch(PongSimulation simulation)
    {
        simulation.Step(PongSimulation.InitialServeDelay, leftInput: 0f, rightInput: 0f);
        Assert.False(simulation.IsServing);
    }

    private static void PlayPoints(PongSimulation simulation, PongSide conceding, int count)
    {
        for (var point = 0; point < count; point++)
        {
            PongMatchRunner.PlayPoint(simulation, conceding);
        }
    }
}
