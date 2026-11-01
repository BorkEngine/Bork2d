using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Runtime;
using Bork2d.Runtime.Sdl;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Characterization tests proving that the windowed and offscreen modes run through the same shared
/// rendered-run lifecycle: the same setup/readiness/tick/teardown ordering, the same renderer
/// start/readiness/cleanup behavior, and the same documented failure precedence.
/// </summary>
/// <remarks>
/// Every test drives the real <see cref="Bork2dApp"/> and host through the fake SDL/renderer seams;
/// no Vulkan or SDL call is made and no test sleeps or polls. The windowed startup-close test
/// synchronizes on the platform's deterministic "window minimized" log entry, which the OS thread
/// emits only after it drained the earlier close/<c>SDL_QUIT</c> event.
/// </remarks>
public sealed class RenderedRunLifecycleTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("Windowed")]
    [InlineData("Offscreen")]
    public void RendererStartFailureRunsSetupZeroTicksTeardownAndJoins(string mode)
    {
        var sdl = new FakeSdlApi();
        var error = new Bork2dException("renderer thread refused to start");
        var renderer = new FakeRenderer { StartFailure = error };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();

        using var app = Build(mode, game, factory, sdl);
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(0, renderer.WaitForReadyCount);
        AssertCleanup(mode, sdl, renderer);
    }

    [Theory]
    [InlineData("Windowed")]
    [InlineData("Offscreen")]
    public void ReadinessFailureRunsTeardownJoinsAndRethrows(string mode)
    {
        var sdl = new FakeSdlApi();
        var error = new Bork2dException("no suitable device");
        var renderer = new FakeRenderer();
        renderer.OnWaitForReady = () => renderer.FailReadiness(error);
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();

        using var app = Build(mode, game, factory, sdl);
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(1, renderer.WaitForReadyCount);
        AssertCleanup(mode, sdl, renderer);
    }

    [Theory]
    [InlineData("Windowed")]
    [InlineData("Offscreen")]
    public void SetupFailureSkipsTeardownAndWinsOverTheRendererFailure(string mode)
    {
        var sdl = new FakeSdlApi();
        var setupError = new InvalidOperationException("setup failed");
        var rendererError = new Bork2dException("no device");
        var renderer = new FakeRenderer { StartFailure = rendererError };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnSetup = _ => throw setupError };

        using var app = Build(mode, game, factory, sdl);
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(setupError, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
        AssertCleanup(mode, sdl, renderer);
    }

    [Theory]
    [InlineData("Windowed")]
    [InlineData("Offscreen")]
    public void ExitDuringSetupSkipsTicksAndRunsTeardownOnce(string mode)
    {
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer();
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnSetup = engine => engine.Exit() };

        using var app = Build(mode, game, factory, sdl);
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(0, renderer.WaitForReadyCount);
        AssertCleanup(mode, sdl, renderer);
    }

    [Theory]
    [InlineData("Windowed")]
    [InlineData("Offscreen")]
    public void TeardownFailurePropagatesAfterTheRun(string mode)
    {
        var sdl = new FakeSdlApi();
        var teardownError = new InvalidOperationException("teardown failed");
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () => game.Engine!.Exit();
        game.OnTeardown = () => throw teardownError;

        using var app = Build(mode, game, factory, sdl);
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(teardownError, failure);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        AssertCleanup(mode, sdl, renderer);
    }

    [Theory]
    [InlineData("Windowed")]
    [InlineData("Offscreen")]
    public void RendererStartAndTeardownFailuresAggregateInPrecedenceOrder(string mode)
    {
        var sdl = new FakeSdlApi();
        var rendererError = new Bork2dException("no device");
        var teardownError = new InvalidOperationException("teardown failed");
        var renderer = new FakeRenderer { StartFailure = rendererError };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnTeardown = () => throw teardownError };

        using var app = Build(mode, game, factory, sdl);
        var failure = AppTestSupport.RunCaptured(app);

        var aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Equal(new Exception[] { rendererError, teardownError }, aggregate.InnerExceptions);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        AssertCleanup(mode, sdl, renderer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowedCloseOrQuitDuringPlatformStartupSkipsSetupAndTeardown(bool quit)
    {
        var sdl = new FakeSdlApi();
        sdl.Enqueue(quit ? SdlEvent.Quit() : SdlEvent.Window(SdlEventType.WindowClose));
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowMinimized));

        var startupEventsProcessed = new ManualResetEventSlim();
        var renderer = new FakeRenderer();
        var factory = new StartupBlockingRendererFactory(startupEventsProcessed, renderer);
        var game = new ScriptedGame<GameLayer>();

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Startup close test")
            .WithSdlApiFactory(() => sdl)
            .WithRendererFactory(factory)
            .WithVulkanInstanceFactory(new FakeVulkanInstanceFactory())
            .WithLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(new StartupSignalLoggerProvider(startupEventsProcessed));
            })
            .WithoutAssets()
            .Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
        Assert.True(renderer.Joined);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
        Assert.True(Assert.Single(sdl.Windows).IsDisposed);
    }

    [Fact]
    public void OffscreenHasNoPlatformClosePath()
    {
        var sdl = new FakeSdlApi();
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowClose));
        sdl.Enqueue(SdlEvent.Quit());
        var sdlInvoked = false;
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
            .WithSdlApiFactory(() =>
            {
                sdlInvoked = true;
                return sdl;
            })
            .Build();

        AppTestSupport.RunToCompletion(app);

        Assert.False(sdlInvoked);
        Assert.Empty(sdl.Calls);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(2, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
    }

    private static Bork2dApp Build(
        string mode,
        ScriptedGame<GameLayer> game,
        FakeRendererFactory factory,
        FakeSdlApi sdl)
    {
        if (IsWindowed(mode))
        {
            return WindowedTestSupport.Builder(sdl, game, factory).Build();
        }

        return new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
    }

    private static void AssertCleanup(string mode, FakeSdlApi sdl, FakeRenderer renderer)
    {
        Assert.True(renderer.Joined, "the renderer must be joined before the run returns or throws.");

        if (IsWindowed(mode))
        {
            var window = Assert.Single(sdl.Windows);
            Assert.True(window.IsDisposed, "the SDL window must be destroyed before the run returns.");
            Assert.True(sdl.QuitCalled, "SDL video must be quit before the run returns.");
            Assert.True(sdl.Disposed, "the SDL adapter must be disposed before the run returns.");
            return;
        }

        Assert.Empty(sdl.Calls);
    }

    private static bool IsWindowed(string mode) => string.Equals(mode, "Windowed", StringComparison.Ordinal);

    /// <summary>
    /// Blocks the host's renderer creation until the platform logged that it processed a later
    /// startup event, which is only possible after it handled the earlier close/<c>SDL_QUIT</c>.
    /// </summary>
    private sealed class StartupBlockingRendererFactory : IRendererFactory
    {
        private readonly ManualResetEventSlim _startupEventsProcessed;
        private readonly FakeRendererFactory _inner;

        internal StartupBlockingRendererFactory(
            ManualResetEventSlim startupEventsProcessed,
            FakeRenderer renderer)
        {
            _startupEventsProcessed = startupEventsProcessed;
            _inner = new FakeRendererFactory(renderer);
        }

        internal int CreateCount => _inner.CreateCount;

        public IRenderer Create(RendererStartInfo startInfo)
        {
            if (!_startupEventsProcessed.Wait(_timeout))
            {
                throw new TimeoutException(
                    "The window platform did not process the startup close/QUIT event within "
                    + $"{_timeout.TotalSeconds:0} s.");
            }

            return _inner.Create(startInfo);
        }
    }

    /// <summary>
    /// Signals when the platform logs the minimized event that follows the startup close/<c>QUIT</c>.
    /// </summary>
    private sealed class StartupSignalLoggerProvider : ILoggerProvider
    {
        private readonly ManualResetEventSlim _startupEventsProcessed;

        internal StartupSignalLoggerProvider(ManualResetEventSlim startupEventsProcessed)
            => _startupEventsProcessed = startupEventsProcessed;

        public ILogger CreateLogger(string categoryName)
            => new StartupSignalLogger(_startupEventsProcessed);

        public void Dispose()
        {
        }

        private sealed class StartupSignalLogger : ILogger
        {
            private readonly ManualResetEventSlim _startupEventsProcessed;

            internal StartupSignalLogger(ManualResetEventSlim startupEventsProcessed)
                => _startupEventsProcessed = startupEventsProcessed;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (formatter(state, exception).Contains("window minimized", StringComparison.Ordinal))
                {
                    _startupEventsProcessed.Set();
                }
            }
        }
    }
}
