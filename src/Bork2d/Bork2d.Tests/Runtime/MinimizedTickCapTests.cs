using Bork2d.Runtime;
using Bork2d.Runtime.Hosting;
using Bork2d.Runtime.Sdl;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Deterministic tests for the minimized-only windowed tick cap (Q8/F4): at most 30 ticks per
/// second while the window is minimized, using the internal scheduler's pure sleep, with no cap in
/// the normal windowed path, no cap offscreen, and no catch-up burst.
/// </summary>
/// <remarks>
/// The fake SDL seam drives the minimize/restore events and <see cref="FakeSchedulerClock"/> records
/// every sleep, so the assertions are exact and no test waits.
/// </remarks>
public sealed class MinimizedTickCapTests
{
    private const double MinimizedPeriodSeconds = 1d / 30d;

    [Fact]
    public void MinimizedWindowSleepsOneThirtiethPerMinimizedTickAndStopsOnRestore()
    {
        var sdl = new FakeSdlApi();
        var clock = new FakeSchedulerClock();
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            switch (game.TickCount)
            {
                case 2:
                    sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowMinimized));
                    WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-minimize");
                    break;

                case 6:
                    sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowRestored));
                    WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-restore");
                    break;

                case 8:
                    game.Engine!.Exit();
                    break;

                default:
                    break;
            }
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory)
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(8, game.TickCount);
        Assert.Equal(8L, renderer.SnapshotCounters.Published);
        Assert.Equal(1, game.TeardownCount);

        // Sleeps after the minimized ticks 3–5: the first observed minimized tick (2) only starts
        // the grid, then three minimized tick-start intervals are kept at 1/30 s. Restoring on
        // tick 6 discards the grid, so ticks 7–8 add none.
        Assert.Equal(3, clock.SleepCount);
        foreach (TimeSpan sleep in clock.SleepSnapshot())
        {
            Assert.InRange(
                sleep.TotalSeconds,
                MinimizedPeriodSeconds - 0.001d,
                MinimizedPeriodSeconds + 0.001d);
        }

        Assert.Equal(3 * MinimizedPeriodSeconds, clock.TotalSleepSeconds, precision: 6);
    }

    [Fact]
    public void NormalWindowedLoopNeverSleeps()
    {
        var sdl = new FakeSdlApi();
        var clock = new FakeSchedulerClock();
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 4)
            {
                game.Engine!.Exit();
            }
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory)
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(4, game.TickCount);
        Assert.Equal(0, clock.SleepCount);
    }

    [Fact]
    public void OffscreenLoopNeverSleeps()
    {
        var clock = new FakeSchedulerClock();
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 4)
            {
                game.Engine!.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(4, game.TickCount);
        Assert.Equal(0, clock.SleepCount);
    }
}
