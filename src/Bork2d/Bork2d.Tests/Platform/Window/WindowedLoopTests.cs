using Bork2d.Engine;
using Bork2d.Engine.Input;
using Bork2d.Engine.Scene;
using Bork2d.Runtime.Hosting;
using Bork2d.Runtime.Sdl;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Platform.Window;

/// <summary>
/// Deterministic tests for the windowed runtime driver: the uncapped loop, exit/close/fatal
/// boundaries, error precedence, teardown while the window is alive, and post-shutdown contracts.
/// </summary>
/// <remarks>
/// Every test drives the real <see cref="AppRuntime{TLayer}"/> and <see cref="SdlWindowPlatform"/>
/// through <see cref="FakeSdlApi"/>; no desktop session or native SDL is involved. App runs happen
/// on a bounded join in <see cref="AppTestSupport"/>, and all fake-platform synchronization uses
/// <see cref="WindowedTestSupport.SyncAfterCommand"/> rather than sleeps or polling.
/// </remarks>
public sealed class WindowedLoopTests
{
    private const string WindowTitle = "Loop test";

    [Fact]
    public void ExitDuringSetupRunsZeroTicksAndTearsDownOnce()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer> { OnSetup = engine => engine.Exit() };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void CloseDuringSetupRunsZeroTicksAndTearsDownOnce()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnSetup = _ =>
        {
            sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowClose));
            WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-close-setup");
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void ExitDuringTickCompletesTheTickAndTearsDownOnce()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () => game.Engine?.Exit();
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void CloseAppliedDuringTickStopsBeforeTheNextFreeze()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount != 1)
            {
                return;
            }

            sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowClose));
            WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-close-tick");
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void AsyncCloseRequestStopsTheLoopAtATickBoundaryAndTearsDownOnce()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 1)
            {
                sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowClose));
            }
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.True(game.TickCount >= 1);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void TextHandlerExceptionFailsLikeATickFailure()
    {
        var sdl = new FakeSdlApi { StartFocused = true };
        var error = new InvalidOperationException("text handler failed");
        var textCalls = 0;
        var game = new ScriptedGame<GameLayer>();
        game.OnSetup = engine =>
        {
            engine.Input.OnTextTyped += _ =>
            {
                Interlocked.Increment(ref textCalls);
                throw error;
            };

            sdl.Enqueue(SdlEvent.TextInput("a"));
            sdl.Enqueue(SdlEvent.TextInput("b"));
            WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-text-setup");
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(1, Volatile.Read(ref textCalls));
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void FatalPlatformErrorMidRunStopsTheLoopTearsDownAndRethrows()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount != 1)
            {
                return;
            }

            sdl.EventPollFailure = "poll boom";
            sdl.Enqueue(SdlEvent.MouseMotion(0, 0));
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        var failure = AppTestSupport.RunCaptured(app);

        var fatal = Assert.IsType<Bork2dException>(failure);
        Assert.Contains("poll boom", fatal.Message, StringComparison.Ordinal);
        Assert.True(game.TickCount >= 1);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void FatalPlatformErrorAndTeardownFailureAggregate()
    {
        var sdl = new FakeSdlApi();
        var teardownError = new InvalidOperationException("teardown failed");
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount != 1)
            {
                return;
            }

            sdl.EventPollFailure = "poll boom";
            sdl.Enqueue(SdlEvent.MouseMotion(0, 0));
        };
        game.OnTeardown = () => throw teardownError;
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        var failure = AppTestSupport.RunCaptured(app);

        var aggregate = Assert.IsType<AggregateException>(failure);
        var fatal = Assert.IsType<Bork2dException>(aggregate.InnerExceptions[0]);
        Assert.Contains("poll boom", fatal.Message, StringComparison.Ordinal);
        Assert.Contains(teardownError, aggregate.InnerExceptions);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowStartFailureThrowsBeforeSetupAndTearsDownNothing()
    {
        var sdl = new FakeSdlApi
        {
            ErrorText = "no display",
            CreateWindowFailure = "SDL_CreateWindow failed: no display",
        };
        var game = new ScriptedGame<GameLayer>();
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        var failure = AppTestSupport.RunCaptured(app);

        var bork2d = Assert.IsType<Bork2dException>(failure);
        Assert.Contains("no display", bork2d.Message, StringComparison.Ordinal);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
        Assert.Empty(sdl.Windows);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
    }

    [Fact]
    public void SetupExceptionPropagatesWithoutTeardownAndShutsThePlatformDown()
    {
        var sdl = new FakeSdlApi();
        var error = new InvalidOperationException("setup failed");
        var game = new ScriptedGame<GameLayer> { OnSetup = _ => throw error };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void TickExceptionRunsTeardownAndRethrowsTheOriginalAfterShutdown()
    {
        var sdl = new FakeSdlApi();
        var error = new InvalidOperationException("tick failed");
        var game = new ScriptedGame<GameLayer> { OnTick = () => throw error };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void TickAndTeardownBothThrowAggregateAfterWindowShutdown()
    {
        var sdl = new FakeSdlApi();
        var tickError = new InvalidOperationException("tick failed");
        var teardownError = new InvalidOperationException("teardown failed");
        var game = new ScriptedGame<GameLayer>
        {
            OnTick = () => throw tickError,
            OnTeardown = () => throw teardownError,
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        var failure = AppTestSupport.RunCaptured(app);

        var aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Contains(tickError, aggregate.InnerExceptions);
        Assert.Contains(teardownError, aggregate.InnerExceptions);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowIsVisibleInSetupAndWindowAndInputAreAliveDuringTeardown()
    {
        var sdl = new FakeSdlApi { StartFocused = true };
        var visibleInSetup = false;
        var aliveDuringTeardown = false;
        var inputReadableDuringTeardown = false;
        var game = new ScriptedGame<GameLayer>();
        game.OnSetup = engine =>
        {
            var window = Assert.Single(sdl.Windows);
            visibleInSetup = window.Flags.HasFlag(SdlWindowFlags.Shown) && !window.IsDisposed;
            Assert.Equal(WindowTitle, engine.Window.Title);
        };
        game.OnTick = () => game.Engine?.Exit();
        game.OnTeardown = () =>
        {
            var window = Assert.Single(sdl.Windows);
            aliveDuringTeardown = !window.IsDisposed;
            var engine = game.Engine
                ?? throw new InvalidOperationException("Setup did not capture the engine.");
            _ = engine.Window.Title;
            inputReadableDuringTeardown = engine.Input.IsKeyDown(KeyCode.A);
            engine.Window.Title = "best-effort teardown title";
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.True(visibleInSetup, "The created window must be visible before Setup runs.");
        Assert.True(aliveDuringTeardown, "Teardown must run before the window is destroyed.");
        Assert.False(inputReadableDuringTeardown);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowIsDestroyedAndTheOsThreadJoinedBeforeRunReturns()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () => game.Engine?.Exit();
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        var window = Assert.Single(sdl.Windows);
        Assert.True(window.IsDisposed);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
        Assert.NotEqual(Environment.CurrentManagedThreadId, sdl.InitializeVideoThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, sdl.CreateWindowThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, sdl.DisposeThreadId);

        var calls = sdl.Calls.ToList();
        Assert.True(
            calls.IndexOf("DestroyWindow") < calls.IndexOf("Dispose"),
            "the window must be destroyed before the SDL adapter is disposed.");
    }

    [Fact]
    public void RetainedWindowAndInputThrowObjectDisposedAfterRunAndTimeStaysReadable()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 2)
            {
                game.Engine?.Exit();
            }
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        var engine = Assert.IsType<GameEngine<GameLayer>>(game.Engine);
        var window = engine.Window;
        var input = engine.Input;

        Assert.Throws<ObjectDisposedException>(() => { _ = window.Title; });
        Assert.Throws<ObjectDisposedException>(() => window.Title = "after shutdown");
        Assert.Throws<ObjectDisposedException>(() => { _ = window.Size; });
        Assert.Throws<ObjectDisposedException>(() => window.Size = new(640f, 360f));
        Assert.Throws<ObjectDisposedException>(() => { _ = window.Fullscreen; });
        Assert.Throws<ObjectDisposedException>(() => window.Fullscreen = true);
        Assert.Throws<ObjectDisposedException>(() => { _ = window.CursorVisible; });
        Assert.Throws<ObjectDisposedException>(() => window.CursorVisible = false);
        Assert.Throws<ObjectDisposedException>(() => { _ = window.IsMinimized; });

        Assert.Throws<ObjectDisposedException>(() => input.IsKeyPressed(KeyCode.A));
        Assert.Throws<ObjectDisposedException>(() => input.IsKeyReleased(KeyCode.A));
        Assert.Throws<ObjectDisposedException>(() => input.IsKeyDown(KeyCode.A));
        Assert.Throws<ObjectDisposedException>(() => { _ = input.MousePosition; });
        Assert.Throws<ObjectDisposedException>(() => { _ = input.MouseDelta; });
        Assert.Throws<ObjectDisposedException>(() => { _ = input.MouseWheelDelta; });
        Assert.Throws<ObjectDisposedException>(() => input.IsMousePressed(MouseButton.Left));
        Assert.Throws<ObjectDisposedException>(() => input.IsMouseReleased(MouseButton.Left));
        Assert.Throws<ObjectDisposedException>(() => input.IsMouseDown(MouseButton.Left));
        Assert.Throws<ObjectDisposedException>(() => input.OnTextTyped += _ => { });

        Assert.True(double.IsFinite(engine.Time.Time));
        Assert.True(double.IsFinite(engine.Time.DeltaTime));
        Assert.InRange(engine.Time.DeltaTime, 0d, 0.25d);
    }

    [Fact]
    public void ExitAfterRunIsANoOp()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () => game.Engine?.Exit();
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        var engine = Assert.IsType<GameEngine<GameLayer>>(game.Engine);
        Assert.False(engine.ExitRequested);

        engine.Exit();

        Assert.False(engine.ExitRequested);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void LoopStopIsLoggedWithReasonTicksElapsedAndFinalFramebuffer()
    {
        var provider = new CapturingLoggerProvider();
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () => game.Engine?.Exit();
        using var app = WindowedTestSupport.Builder(sdl, game)
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();

        AppTestSupport.RunToCompletion(app);

        var entry = Assert.Single(
            provider.Entries,
            captured => captured.Level == LogLevel.Information
                && captured.Message.Contains("windowed loop stopped", StringComparison.Ordinal));

        Assert.Equal(Bork2dLogging.RuntimeCategory, entry.Category);
        Assert.Contains("reason=Exit", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ticks=1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("elapsedSeconds=", entry.Message, StringComparison.Ordinal);
        Assert.Contains("finalFramebuffer=1280x720", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunWithoutExplicitStopLoopsUntilExitAndSecondRunThrows()
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 3)
            {
                game.Engine?.Exit();
            }
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Equal(3, game.TickCount);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Throws<InvalidOperationException>(() => app.Run());
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void DisposeDuringRunThrowsAndTheLoopContinuesUntilExit()
    {
        var sdl = new FakeSdlApi();
        var firstTick = new ManualResetEventSlim();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () => firstTick.Set();
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        Exception? runFailure = null;
        var thread = new Thread(() =>
        {
            try
            {
                app.Run();
            }
            catch (Exception exception)
            {
                runFailure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "Bork2d.Tests.DisposeDuringRun",
        };
        thread.Start();

        try
        {
            Assert.True(
                firstTick.Wait(TimeSpan.FromSeconds(20)),
                "The windowed loop did not start ticking.");

            Assert.Throws<InvalidOperationException>(() => app.Dispose());
            Assert.True(Volatile.Read(ref runFailure) is null, "Dispose must not disturb the loop.");

            game.Engine!.Exit();
        }
        finally
        {
            Assert.True(
                thread.Join(TimeSpan.FromSeconds(20)),
                "The windowed loop did not stop after IEngine.Exit().");
        }

        Assert.Null(runFailure);
        Assert.True(game.TickCount >= 1);
        Assert.Equal(1, game.TeardownCount);
        AssertPlatformShutdown(sdl);

        // Now that the run has returned, disposal is allowed and idempotent.
        app.Dispose();
        app.Dispose();
    }

    private static void AssertPlatformShutdown(FakeSdlApi sdl)
    {
        var window = Assert.Single(sdl.Windows);
        Assert.True(window.IsDisposed, "The SDL window must be destroyed before Run returns.");
        Assert.True(sdl.QuitCalled, "SDL video must be quit before Run returns.");
        Assert.True(sdl.Disposed, "The SDL adapter must be disposed before Run returns.");
        Assert.NotEqual(Environment.CurrentManagedThreadId, sdl.InitializeVideoThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, sdl.CreateWindowThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, sdl.DisposeThreadId);
    }
}
