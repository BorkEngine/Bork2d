using Bork2d.Engine;
using Bork2d.Engine.Input;
using Bork2d.Render;
using Bork2d.Render.Handoff;
using Bork2d.Runtime;
using Bork2d.Runtime.Hosting;
using Bork2d.Runtime.Sdl;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Deterministic host tests for the M7 windowed/offscreen handoff wiring (WP-4): the per-tick
/// acquire → freeze/time/text → tick → capture → publish order, backpressure, the blocked-producer
/// wake paths (Q9=I1), publication of the final Exit/close tick (Q4=D1), slot abandonment on a tick
/// failure, and the one-shot loop-stop log.
/// </summary>
/// <remarks>
/// Every test drives the real <see cref="AppRuntime{TLayer}"/> through <see cref="FakeRenderer"/> and
/// the fake SDL/Vulkan seams. Concurrency tests synchronize on the fake renderer's event-based
/// counters (never sleeps or polling) and join the app thread with a bounded timeout.
/// </remarks>
public sealed class HostHandoffTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public void WindowedTickAcquiresBeforeTheTickAndPublishesAfterCapture()
    {
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        double? firstTickTime = null;
        double? secondTickTime = null;
        double? lastTickTime = null;
        double? lastTickDelta = null;
        game.OnTick = () =>
        {
            var engine = game.Engine!;

            // Acquire happened before this tick; every earlier tick is already published because
            // capture+publish run after Tick returns.
            Assert.Equal(game.TickCount, renderer.AcquireCount);
            Assert.Equal(game.TickCount - 1, renderer.SnapshotCounters.Published);

            switch (game.TickCount)
            {
                case 1:
                    firstTickTime = engine.Time.Time;
                    break;

                case 2:
                    secondTickTime = engine.Time.Time;
                    break;

                default:
                    lastTickTime = engine.Time.Time;
                    lastTickDelta = engine.Time.DeltaTime;
                    engine.Exit();
                    break;
            }
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(0d, firstTickTime);
        Assert.True(secondTickTime > firstTickTime, "NewFrame must advance ITime before Tick.");
        Assert.Equal(3, game.TickCount);
        Assert.Equal(1, game.TeardownCount);

        // The last published snapshot is the Exit tick's (Q4=D1), captured after Tick with that
        // tick's ITime values and a sequence that started at 1.
        Assert.Equal(3L, renderer.LastPublishedSequence);
        Assert.Equal(lastTickTime!.Value, renderer.LastPublishedTime, precision: 12);
        Assert.Equal(lastTickDelta!.Value, renderer.LastPublishedDeltaTime, precision: 12);

        Assert.Equal(3L, renderer.SnapshotCounters.Published);
        Assert.Equal(3L, renderer.SnapshotCounters.Consumed);
        Assert.Equal(0, renderer.AbandonCount);
        Assert.True(renderer.Joined);

        // Join is the quiescent point: every slot is Free and nothing is latest.
        AssertAllSlotsFree(renderer);
    }

    [Fact]
    public void OffscreenLoopWaitsOnlyWhenTheRingIsFull()
    {
        var renderer = new FakeRenderer { AutoReady = true, AutoConsume = false };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 5)
            {
                game.Engine!.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
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
            Name = "Bork2d.Tests.OffscreenBackpressure",
        };
        thread.Start();

        try
        {
            Assert.True(
                renderer.WaitFor(() => renderer.AcquireBlockedCount >= 1, _timeout),
                "The producer never waited for a free slot with all three slots occupied.");
            Assert.Equal(3, game.TickCount);

            renderer.ReleaseHeldSnapshots();
        }
        finally
        {
            Assert.True(thread.Join(_timeout), "The offscreen loop did not finish.");
        }

        Assert.Null(failure);
        Assert.Equal(5, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(5L, renderer.SnapshotCounters.Published);
        Assert.True(renderer.SnapshotCounters.Consumed <= renderer.SnapshotCounters.Published);
        Assert.True(renderer.AcquireBlockedCount >= 1);
        Assert.True(renderer.Joined);
        AssertAllSlotsFree(renderer);
    }

    [Fact]
    public void OffscreenLoopDoesNotWaitWhenSlotsAreFree()
    {
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
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
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(3, game.TickCount);
        Assert.Equal(0, renderer.AcquireBlockedCount);
        Assert.Equal(3L, renderer.SnapshotCounters.Published);

        // One frame per consumed snapshot: the immediate consumer keeps up with the producer.
        Assert.Equal(3L, renderer.SnapshotCounters.Consumed);
    }

    [Fact]
    public void WindowedBackpressureKeepsInputEdgesForTheNextFreeze()
    {
        var sdl = new FakeSdlApi { StartFocused = true };
        var renderer = new FakeRenderer { AutoReady = true, AutoConsume = false };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        var pressedAfterWait = false;
        var downAfterWait = false;
        game.OnTick = () =>
        {
            if (game.TickCount != 4)
            {
                return;
            }

            var input = game.Engine!.Input;
            pressedAfterWait = input.IsKeyPressed(KeyCode.W);
            downAfterWait = input.IsKeyDown(KeyCode.W);
            game.Engine.Exit();
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();

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
            Name = "Bork2d.Tests.WindowedBackpressure",
        };
        thread.Start();

        try
        {
            Assert.True(
                renderer.WaitFor(() => renderer.AcquireBlockedCount >= 1, _timeout),
                "The windowed producer never waited for a free slot.");
            Assert.Equal(3, game.TickCount);

            // Deliver a key while the tick is backpressured, then prove the OS thread applied it
            // before releasing the ring: the event must belong to the next freeze.
            sdl.Enqueue(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.W));
            WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-key-during-backpressure");

            renderer.ReleaseHeldSnapshots();
        }
        finally
        {
            Assert.True(thread.Join(_timeout), "The windowed loop did not finish.");
        }

        Assert.Null(failure);
        Assert.True(pressedAfterWait, "An input edge that arrived while blocked must not be lost.");
        Assert.True(downAfterWait, "The held key must be down at the next freeze.");
        Assert.Equal(4, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(4L, renderer.SnapshotCounters.Published);
        Assert.True(renderer.Joined);
        AssertAllSlotsFree(renderer);
    }

    [Fact]
    public void ExitWhileBlockedWakesTheProducerAndStartsNoFurtherTick()
    {
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer { AutoReady = true, AutoConsume = false };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();

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
            Name = "Bork2d.Tests.ExitWhileBlocked",
        };
        thread.Start();

        try
        {
            Assert.True(
                renderer.WaitFor(() => renderer.AcquireBlockedCount >= 1, _timeout),
                "The windowed producer never waited for a free slot.");

            // Q9=I1: the engine's exit callback wakes the blocked acquire; no extra tick starts.
            game.Engine!.Exit();
        }
        finally
        {
            Assert.True(thread.Join(_timeout), "The windowed loop did not stop after Exit.");
        }

        Assert.Null(failure);
        Assert.Equal(3, game.TickCount);
        Assert.Equal(3L, renderer.SnapshotCounters.Published);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(1L, renderer.SnapshotCounters.ExitWakes);
        Assert.True(renderer.Joined);
        AssertAllSlotsFree(renderer);
    }

    [Fact]
    public void RendererFatalWhileBlockedIsRethrownWithNoExtraTick()
    {
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer { AutoReady = true, AutoConsume = false };
        var factory = new FakeRendererFactory(renderer);
        var error = new Bork2dException("device lost while blocked");
        var game = new ScriptedGame<GameLayer>();

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();

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
            Name = "Bork2d.Tests.FatalWhileBlocked",
        };
        thread.Start();

        try
        {
            Assert.True(
                renderer.WaitFor(() => renderer.AcquireBlockedCount >= 1, _timeout),
                "The windowed producer never waited for a free slot.");
            renderer.PublishFatal(error);
        }
        finally
        {
            Assert.True(thread.Join(_timeout), "The windowed loop did not stop after the fatal.");
        }

        Assert.Same(error, failure);
        Assert.Equal(3, game.TickCount);
        Assert.Equal(3L, renderer.SnapshotCounters.Published);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
        AssertAllSlotsFree(renderer);
    }

    [Fact]
    public void TickFailureAbandonsTheSlotAndPublishesNothing()
    {
        var error = new InvalidOperationException("tick failed");
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 2)
            {
                throw error;
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(2, game.TickCount);
        Assert.Equal(1, renderer.AbandonCount);
        Assert.Equal(1L, renderer.SnapshotCounters.Published);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
        AssertAllSlotsFree(renderer);
    }

    [Fact]
    public void FinalExitTickIsPublished()
    {
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
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
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
        AppTestSupport.RunToCompletion(app);

        // Q4=D1: the Exit tick still captures and publishes its snapshot.
        Assert.Equal(3, game.TickCount);
        Assert.Equal(3L, renderer.SnapshotCounters.Published);
        Assert.Equal(0, renderer.AbandonCount);
        Assert.Equal(1, game.TeardownCount);
        AssertAllSlotsFree(renderer);
    }

    [Fact]
    public void CloseTickIsPublished()
    {
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount != 3)
            {
                return;
            }

            sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowClose));
            WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-close-tick");
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(3, game.TickCount);
        Assert.Equal(3L, renderer.SnapshotCounters.Published);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
        AssertAllSlotsFree(renderer);
    }

    [Fact]
    public void WindowedLoopStopLogCarriesTheHandoffCounters()
    {
        var provider = new CapturingLoggerProvider();
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 2)
            {
                game.Engine!.Exit();
            }
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory)
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();
        AppTestSupport.RunToCompletion(app);

        var entry = Assert.Single(
            provider.Entries,
            captured => captured.Level == LogLevel.Information
                && captured.Message.Contains("windowed loop stopped", StringComparison.Ordinal));
        Assert.Contains("reason=Exit", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ticks=2", entry.Message, StringComparison.Ordinal);
        Assert.Contains("backpressureWaits=0", entry.Message, StringComparison.Ordinal);
        Assert.Contains("maxLiveSnapshots=", entry.Message, StringComparison.Ordinal);
        Assert.Contains("suspendedConsumptions=0", entry.Message, StringComparison.Ordinal);
        Assert.Contains("finalFramebuffer=", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OffscreenLoopStopLogCarriesTheHandoffCounters()
    {
        var provider = new CapturingLoggerProvider();
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
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
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();
        AppTestSupport.RunToCompletion(app);

        var entry = Assert.Single(
            provider.Entries,
            captured => captured.Level == LogLevel.Information
                && captured.Message.Contains("offscreen loop stopped", StringComparison.Ordinal));
        Assert.Contains("reason=Exit", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ticks=2", entry.Message, StringComparison.Ordinal);
        Assert.Contains("backpressureWaits=0", entry.Message, StringComparison.Ordinal);
        Assert.Contains("maxLiveSnapshots=", entry.Message, StringComparison.Ordinal);
        Assert.Contains("suspendedConsumptions=0", entry.Message, StringComparison.Ordinal);
    }

    private static void AssertAllSlotsFree(FakeRenderer renderer)
    {
        foreach (SnapshotSlot slot in renderer.SnapshotRing.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }

        Assert.Equal(-1, renderer.SnapshotRing.LatestIndex);
    }
}
