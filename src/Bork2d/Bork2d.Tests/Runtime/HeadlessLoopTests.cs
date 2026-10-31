using Bork2d.Engine;
using Bork2d.Runtime;
using Bork2d.Runtime.Hosting;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Deterministic tests for the M7 headless driver (Q11=K1, Q12=L2, Q13=M4/F1, F5–F8): the paced
/// loop shape, the synthesized clock, the strict grid, catch-up, the positive-rate rejection, the
/// no-clamp rule, exit paths, error precedence, and the one-shot stop log.
/// </summary>
/// <remarks>
/// Every schedule assertion uses <see cref="FakeSchedulerClock"/>, so no test sleeps except the
/// explicitly real-time tolerant test in <c>HeadlessPacingRealTimeTests</c>.
/// </remarks>
public sealed class HeadlessLoopTests
{
    [Fact]
    public void ExitDuringSetupRunsZeroTicksAndTearsDownOnce()
    {
        var clock = new FakeSchedulerClock();
        var game = new ScriptedGame<GameLayer> { OnSetup = engine => engine.Exit() };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(0, clock.SleepCount);
    }

    [Fact]
    public void ExitDuringTickCompletesTheTickThenStopsBeforeTheNext()
    {
        var clock = new FakeSchedulerClock();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 2)
            {
                game.Engine!.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(2, game.TickCount);
        Assert.Equal(1, game.TeardownCount);

        // The pacer slept once before tick 1 and not after the Exit tick.
        Assert.Equal(1, clock.SleepCount);
    }

    [Fact]
    public void FirstTickIsZeroAndLaterTicksUseTheSynthesizedStep()
    {
        var clock = new FakeSchedulerClock();
        var times = new List<double>();
        var deltas = new List<double>();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            times.Add(game.Engine!.Time.Time);
            deltas.Add(game.Engine.Time.DeltaTime);
            if (game.TickCount == 4)
            {
                game.Engine.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless(ticksPerSecond: 25)
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        // F5/L2: tick 0 = 0/0; tick N >= 1 = N / rate and 1 / rate.
        Assert.Equal(4, times.Count);
        Assert.Equal(0d, times[0], precision: 12);
        Assert.Equal(0d, deltas[0], precision: 12);
        for (var index = 1; index < times.Count; index++)
        {
            Assert.Equal(index / 25d, times[index], precision: 12);
            Assert.Equal(1d / 25d, deltas[index], precision: 12);
        }
    }

    [Fact]
    public void MaxDeltaTimeDoesNotClampTheSynthesizedStep()
    {
        var clock = new FakeSchedulerClock();
        var deltas = new List<double>();
        var times = new List<double>();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            deltas.Add(game.Engine!.Time.DeltaTime);
            times.Add(game.Engine.Time.Time);
            if (game.TickCount == 2)
            {
                game.Engine.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless(ticksPerSecond: 4)
            .WithoutAssets()
            .WithMaxDeltaTime(0.001)
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        // F7: the exact 1 / 4 step applies even below 4 Hz, regardless of WithMaxDeltaTime.
        Assert.Equal(0d, deltas[0]);
        Assert.Equal(0.25d, deltas[1], precision: 12);
        Assert.Equal(0.25d, times[1], precision: 12);
    }

    [Fact]
    public void StrictGridSleepsOnlyThePositiveRemainder()
    {
        var clock = new FakeSchedulerClock();
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
            .WithHeadless(ticksPerSecond: 20)
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(4, game.TickCount);
        Assert.Equal(3, clock.SleepCount);
        foreach (TimeSpan sleep in clock.SleepSnapshot())
        {
            Assert.InRange(sleep.TotalSeconds, 0.049d, 0.051d);
        }
    }

    [Fact]
    public void CatchUpAfterAStallRunsEveryMissedTickWithoutSleeping()
    {
        var clock = new FakeSchedulerClock();
        var sleepsAtEntry = new List<int>();
        var times = new List<double>();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            sleepsAtEntry.Add(clock.SleepCount);
            times.Add(game.Engine!.Time.Time);

            if (game.TickCount == 3)
            {
                // A 0.35 s stall at 10 Hz: ticks 3–5 are already overdue and run back-to-back.
                clock.Advance(0.35d);
            }

            if (game.TickCount == 6)
            {
                game.Engine.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless(ticksPerSecond: 10)
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(6, game.TickCount);

        // One sleep before tick 1 and one before tick 2; the catch-up ticks 3–5 run with none
        // (F8 unbounded catch-up), and the Exit tick adds none.
        Assert.Equal([0, 1, 2, 2, 2, 2], sleepsAtEntry);
        Assert.Equal(2, clock.SleepCount);
        Assert.Equal(0.2d, clock.TotalSleepSeconds, precision: 6);

        // The synthesized clock advances one exact step per tick, so it catches up with the
        // schedule even though the system clock jumped.
        Assert.Equal(6, times.Count);
        Assert.Equal(0.3d, times[3], precision: 12);
        Assert.Equal(0.4d, times[4], precision: 12);
        Assert.Equal(0.5d, times[5], precision: 12);
    }

    [Fact]
    public void ExitFromAnotherThreadWhileSleepingStopsBeforeTheNextTick()
    {
        var clock = new FakeSchedulerClock();
        var sleeping = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        clock.OnSleep = _ =>
        {
            sleeping.Set();
            release.Wait(TimeSpan.FromSeconds(20));
        };

        var game = new ScriptedGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless(ticksPerSecond: 20)
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                app.Run();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "Bork2d.Tests.HeadlessExitWhileSleeping",
        };
        thread.Start();

        try
        {
            Assert.True(
                sleeping.Wait(TimeSpan.FromSeconds(20)),
                "The headless pacer did not reach its first sleep.");
            game.Engine!.Exit();
            release.Set();
        }
        finally
        {
            release.Set();
            Assert.True(
                thread.Join(TimeSpan.FromSeconds(20)),
                "The headless loop did not stop after Exit while sleeping.");
        }

        Assert.Null(failure);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(1, clock.SleepCount);
    }

    [Fact]
    public void SetupFailureRunsNoTickAndNoTeardown()
    {
        var error = new InvalidOperationException("setup failed");
        var clock = new FakeSchedulerClock();
        var game = new ScriptedGame<GameLayer> { OnSetup = _ => throw error };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
    }

    [Fact]
    public void TickFailureTearsDownAndRethrowsTheOriginal()
    {
        var error = new InvalidOperationException("tick failed");
        var clock = new FakeSchedulerClock();
        var game = new ScriptedGame<GameLayer> { OnTick = () => throw error };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void TickAndTeardownFailuresAggregate()
    {
        var tickError = new InvalidOperationException("tick failed");
        var teardownError = new InvalidOperationException("teardown failed");
        var clock = new FakeSchedulerClock();
        var game = new ScriptedGame<GameLayer>
        {
            OnTick = () => throw tickError,
            OnTeardown = () => throw teardownError,
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        var failure = AppTestSupport.RunCaptured(app);

        var aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Contains(tickError, aggregate.InnerExceptions);
        Assert.Contains(teardownError, aggregate.InnerExceptions);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void TeardownFailureAfterANormalExitPropagates()
    {
        var error = new InvalidOperationException("teardown failed");
        var clock = new FakeSchedulerClock();
        var game = new ScriptedGame<GameLayer> { ExitAfterFirstTick = true, OnTeardown = () => throw error };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void SynthesizedValuesStayReadableAfterShutdown()
    {
        var clock = new FakeSchedulerClock();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 3)
            {
                game.Engine!.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless(ticksPerSecond: 50)
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        var engine = game.Engine
            ?? throw new InvalidOperationException("Setup did not capture the engine.");
        Assert.Equal(2 / 50d, engine.Time.Time, precision: 12);
        Assert.Equal(1 / 50d, engine.Time.DeltaTime, precision: 12);
    }

    [Fact]
    public void HeadlessStopLogIsOneInformationLineWithTicksElapsedRateAndReason()
    {
        var provider = new CapturingLoggerProvider();
        var clock = new FakeSchedulerClock();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 2)
            {
                game.Engine!.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless(ticksPerSecond: 40)
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();
        AppTestSupport.RunToCompletion(app);

        var entry = Assert.Single(
            provider.Entries,
            captured => captured.Level == LogLevel.Information
                && captured.Message.Contains("headless loop stopped", StringComparison.Ordinal));
        Assert.Equal(Bork2dLogging.RuntimeCategory, entry.Category);
        Assert.Contains("reason=Exit", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ticks=2", entry.Message, StringComparison.Ordinal);
        Assert.Contains("elapsedSeconds=", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ticksPerSecond=40", entry.Message, StringComparison.Ordinal);
    }
}
