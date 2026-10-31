using System.Diagnostics;
using Bork2d.Runtime;
using Bork2d.Tests.TestDoubles;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// The one small real-time tolerant test for the production headless pacer (Q21=U1).
/// </summary>
/// <remarks>
/// Every schedule assertion elsewhere uses <see cref="FakeSchedulerClock"/>. This test exercises the
/// real <c>SystemSchedulerClock</c> (<c>Stopwatch</c> plus pure <c>Thread.Sleep</c>) and asserts only
/// a generous window: at least half the nominal schedule elapsed before the final tick and an upper
/// bound that cannot flake on a slow machine. It never asserts exact frame counts or durations.
/// </remarks>
public sealed class HeadlessPacingRealTimeTests
{
    [Fact]
    public void RealPacerHonorsTheConfiguredRateWithinATolerantWindow()
    {
        const int ticks = 4;
        const int rate = 40;
        const double period = 1d / rate;

        var times = new List<double>();
        var deltas = new List<double>();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            times.Add(game.Engine!.Time.Time);
            deltas.Add(game.Engine.Time.DeltaTime);
            if (game.TickCount == ticks)
            {
                game.Engine.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless(ticksPerSecond: rate)
            .WithoutAssets()
            .Build();

        var stopwatch = Stopwatch.StartNew();
        app.Run();
        stopwatch.Stop();

        Assert.Equal(ticks, game.TickCount);
        Assert.Equal(1, game.TeardownCount);

        // The synthesized values stay exact on the real pacer too (F5/L2).
        Assert.Equal(0d, times[0]);
        Assert.Equal(0d, deltas[0]);
        for (var index = 1; index < ticks; index++)
        {
            Assert.Equal(index * period, times[index], precision: 9);
            Assert.Equal(period, deltas[index], precision: 9);
        }

        // Tolerant real-time window: the nominal grid spans (ticks - 1) periods. A pure Sleep can
        // overshoot, so only a generous lower bound and a loose upper bound are asserted.
        var nominal = (ticks - 1) * period;
        Assert.True(
            stopwatch.Elapsed.TotalSeconds >= nominal * 0.5d,
            $"The real pacer returned after {stopwatch.Elapsed.TotalSeconds:0.###} s, less than half "
                + $"the nominal {nominal:0.###} s schedule.");
        Assert.True(
            stopwatch.Elapsed.TotalSeconds < nominal + 5d,
            $"The real pacer took {stopwatch.Elapsed.TotalSeconds:0.###} s, unexpectedly longer "
                + $"than the nominal {nominal:0.###} s schedule.");
    }
}
