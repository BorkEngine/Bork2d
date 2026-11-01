using System.Reflection;
using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Textures;
using Bork2d.Runtime;
using Bork2d.Runtime.Hosting;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Deterministic host-lifecycle tests for the renderer seam: startup ordering, the first-tick
/// readiness gate, the offscreen loop (D-M6-07), and the M6 error precedence (D-M6-14).
/// </summary>
/// <remarks>
/// Every test uses <see cref="FakeRenderer"/> and the fake SDL/Vulkan seams; no Vulkan or SDL call
/// is made and no test sleeps or polls. Readiness is signalled from the fake's wait callback, which
/// also proves the host ordering.
/// </remarks>
public sealed class RendererHostTests
{
    [Fact]
    public void SetupRunsBeforeReadinessAndNoTickRunsBeforeReady()
    {
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer();
        var factory = new FakeRendererFactory(renderer);
        var instanceFactory = new FakeVulkanInstanceFactory();
        var events = new List<string>();
        var game = new ScriptedGame<GameLayer>();
        game.OnSetup = _ =>
        {
            events.Add("setup");
        };
        game.OnTick = () =>
        {
            events.Add("tick");
            game.Engine!.Exit();
        };
        renderer.OnWaitForReady = () =>
        {
            Assert.Equal(["setup"], events);
            Assert.Equal(0, game.TickCount);
            Assert.False(renderer.IsReady);
            events.Add("wait");
            renderer.SetReady();
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory, instanceFactory).Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(["setup", "wait", "tick"], events);
        Assert.Equal(1, renderer.WaitForReadyCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);

        RendererStartInfo startInfo = Assert.IsType<RendererStartInfo>(factory.StartInfo);
        Assert.Equal(RendererMode.Windowed, startInfo.Mode);
        Assert.NotNull(startInfo.Instance);
        Assert.NotEqual(0, startInfo.SurfaceHandle);
        Assert.Equal(1280, startInfo.Width);
        Assert.Equal(720, startInfo.Height);
        Assert.True(startInfo.ValidationRequested);
        Assert.Same(game.Engine!.Assets, startInfo.Assets);
        Assert.Same(game.Engine.Assets, startInfo.AssetCatalogVersion);
        Assert.Equal(
            TexturePageStore.DefaultResidencyBudgetBytes,
            startInfo.TextureResidencyBudgetBytes);
        Assert.Equal(TexturePageStore.DefaultUploadBudgetBytes, startInfo.TextureUploadBudgetBytes);
        Assert.NotNull(instanceFactory.LastRequest);
        Assert.True(instanceFactory.LastRequest!.ValidationRequested);
        Assert.Equal("Loop test", instanceFactory.LastRequest.ApplicationName);
        Assert.Equal("Bork2d", instanceFactory.LastRequest.EngineName);
        Assert.Contains("VK_KHR_surface", instanceFactory.LastRequest.RequiredExtensions);
    }

    [Fact]
    public void TheHostInstallsTheRenderersStatsSnapshot()
    {
        var sdl = new FakeSdlApi();
        var expected = new Bork2d.Engine.Rendering.RenderStats(
            FrameSequence: 4,
            FramebufferWidth: 1280,
            FramebufferHeight: 720,
            DrawCalls: 1,
            Instances: 3,
            Cameras: 1,
            NonEmptyCameras: 1,
            TexturePages: 2,
            TexturePageCapacity: 256,
            TextureUploadsPending: 0,
            TextureUploadBytesPending: 0,
            SkippedSprites: 0,
            ValidationErrors: 0,
            ValidationWarnings: 0);
        var renderer = new FakeRenderer { AutoReady = true, RenderStats = expected };
        var factory = new FakeRendererFactory(renderer);
        var instanceFactory = new FakeVulkanInstanceFactory();
        var observed = default(Bork2d.Engine.Rendering.RenderStats);
        var game = new ScriptedGame<GameLayer>();
        game.OnSetup = engine => Assert.Equal(expected, engine.RenderStats);
        game.OnTick = () =>
        {
            observed = game.Engine!.RenderStats;
            game.Engine!.Exit();
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory, instanceFactory).Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(expected, observed);
        Assert.Equal(1, game.TickCount);
    }

    [Fact]
    public void WindowedReadinessFailureRunsTeardownThrowsAndJoins()
    {
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer();
        var factory = new FakeRendererFactory(renderer);
        var error = new Bork2dException("no suitable device");
        var game = new ScriptedGame<GameLayer>();
        renderer.OnWaitForReady = () => renderer.FailReadiness(error);

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.StopRequested);
        Assert.True(renderer.Joined);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowedSynchronousStartFailureStillRunsSetupAndTearsDown()
    {
        var sdl = new FakeSdlApi();
        var error = new Bork2dException("renderer thread refused to start");
        var renderer = new FakeRenderer { StartFailure = error };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(0, renderer.WaitForReadyCount);
        Assert.True(renderer.Joined);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowedFactoryFailureStillRunsSetupAndTearsDown()
    {
        var sdl = new FakeSdlApi();
        var error = new Bork2dException("no renderer factory");
        var factory = new FakeRendererFactory { CreateException = error };
        var game = new ScriptedGame<GameLayer>();

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.False(factory.Renderer.Started);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowedSetupFailureWinsOverTheRendererFailureAndRunsNoTeardown()
    {
        var sdl = new FakeSdlApi();
        var setupError = new InvalidOperationException("setup failed");
        var rendererError = new Bork2dException("no device");
        var renderer = new FakeRenderer { StartFailure = rendererError };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnSetup = _ => throw setupError };

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(setupError, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
        Assert.True(renderer.Joined);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowedPostStartFatalStopsAtTheNextBoundaryTearsDownAndRethrows()
    {
        var sdl = new FakeSdlApi();
        var error = new Bork2dException("device lost");
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 1)
            {
                renderer.PublishFatal(error);
            }
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowedTeardownRunsBeforeTheRendererIsStoppedAndJoined()
    {
        var sdl = new FakeSdlApi();
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () => game.Engine!.Exit();
        game.OnTeardown = () => Assert.False(
            renderer.Joined,
            "Teardown must run before the renderer is stopped/joined.");

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void WindowedTickAndTeardownFailuresAggregateAfterTheRendererJoins()
    {
        var sdl = new FakeSdlApi();
        var tickError = new InvalidOperationException("tick failed");
        var teardownError = new InvalidOperationException("teardown failed");
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>
        {
            OnTick = () => throw tickError,
            OnTeardown = () => throw teardownError,
        };

        using var app = WindowedTestSupport.Builder(sdl, game, factory).Build();
        var failure = AppTestSupport.RunCaptured(app);

        var aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Contains(tickError, aggregate.InnerExceptions);
        Assert.Contains(teardownError, aggregate.InnerExceptions);
        Assert.True(renderer.Joined);
        AssertPlatformShutdown(sdl);
    }

    [Fact]
    public void TextureBudgetsFlowFromTheBuilderIntoTheRendererStartInfo()
    {
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnSetup = engine => engine.Exit() };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithTextureResidencyBudget(8L * 1024 * 1024)
            .WithTextureUploadBudget(64 * 1024)
            .WithFakeGraphics(factory)
            .Build();
        AppTestSupport.RunToCompletion(app);

        RendererStartInfo startInfo = factory.StartInfo!;
        Assert.Equal(8L * 1024 * 1024, startInfo.TextureResidencyBudgetBytes);
        Assert.Equal(64 * 1024, startInfo.TextureUploadBudgetBytes);
        Assert.Same(game.Engine!.Assets, startInfo.AssetCatalogVersion);
    }

    [Fact]
    public void TheDefaultTexturePageStartupReservationIsThePolicyValue()
    {
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnSetup = engine => engine.Exit() };
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory);

        Assert.Equal(
            TexturePageCapacity.StartupReservation,
            builder.Options.TexturePageStartupReservation);

        using var app = builder.Build();
        AppTestSupport.RunToCompletion(app);

        RendererStartInfo startInfo = factory.StartInfo!;
        Assert.Equal(4096, startInfo.TexturePageStartupReservation);
    }

    [Fact]
    public void TheInternalTexturePageStartupReservationFlowsIntoTheStartInfo()
    {
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnSetup = engine => engine.Exit() };
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory);
        builder.Options.TexturePageStartupReservation = TexturePageCapacity.Initial;

        using var app = builder.Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(
            TexturePageCapacity.Initial,
            factory.StartInfo!.TexturePageStartupReservation);
    }

    [Fact]
    public void ThePublicBuilderDoesNotExposeTheTexturePageStartupReservation()
    {
        MemberInfo[] publicMembers = typeof(Bork2dAppBuilder<GameLayer>).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        Assert.DoesNotContain(
            publicMembers,
            member => member.Name.Contains("StartupReservation", StringComparison.Ordinal));

        PropertyInfo? option = typeof(Bork2dAppOptions<GameLayer>).GetProperty(
            nameof(Bork2dAppOptions<GameLayer>.TexturePageStartupReservation),
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(option);
        Assert.False(option!.GetMethod!.IsPublic);
        Assert.False(option.SetMethod!.IsPublic);
    }

    [Fact]
    public void OffscreenLoopRunsTicksUntilExitAdvancesTimeAndTearsDownOnce()
    {
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        double? firstTime = null;
        double? lastTime = null;
        game.OnTeardown = () => Assert.False(
            renderer.Joined,
            "Teardown must run before the renderer is stopped/joined.");
        game.OnTick = () =>
        {
            var engine = game.Engine!;
            if (game.TickCount == 1)
            {
                firstTime = engine.Time.Time;
            }

            lastTime = engine.Time.Time;
            if (game.TickCount == 3)
            {
                engine.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(3, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(0d, firstTime);
        Assert.True(lastTime!.Value > firstTime!.Value, "Offscreen ITime must advance per tick.");
        Assert.True(renderer.Joined);
        Assert.Equal(RendererStatus.Stopped, renderer.Status);

        RendererStartInfo startInfo = factory.StartInfo!;
        Assert.Equal(RendererMode.Offscreen, startInfo.Mode);
        Assert.Null(startInfo.Instance);
        Assert.Equal(0, startInfo.SurfaceHandle);
        Assert.Equal(320, startInfo.Width);
        Assert.Equal(240, startInfo.Height);
    }

    [Fact]
    public void OffscreenExitDuringSetupRunsZeroTicksAndSkipsTheReadinessWait()
    {
        var renderer = new FakeRenderer();
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnSetup = engine => engine.Exit() };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(0, renderer.WaitForReadyCount);
        Assert.True(renderer.Joined);
    }

    [Fact]
    public void OffscreenReadinessFailureRunsTeardownThrowsAndJoins()
    {
        var renderer = new FakeRenderer();
        var factory = new FakeRendererFactory(renderer);
        var error = new Bork2dException("no suitable offscreen device");
        var game = new ScriptedGame<GameLayer>();
        renderer.OnWaitForReady = () => renderer.FailReadiness(error);

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
    }

    [Fact]
    public void OffscreenFactoryFailureStillRunsSetupAndTearsDown()
    {
        var error = new Bork2dException("no renderer factory");
        var factory = new FakeRendererFactory { CreateException = error };
        var game = new ScriptedGame<GameLayer>();

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void OffscreenSetupFailureWinsOverTheRendererFailureAndRunsNoTeardown()
    {
        var setupError = new InvalidOperationException("setup failed");
        var rendererError = new Bork2dException("no device");
        var renderer = new FakeRenderer { StartFailure = rendererError };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer> { OnSetup = _ => throw setupError };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();
        var failure = AppTestSupport.RunCaptured(app);

        Assert.Same(setupError, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
        Assert.True(renderer.Joined);
    }

    [Fact]
    public void OffscreenPostStartFatalStopsAtTheNextBoundaryTearsDownAndRethrows()
    {
        var error = new Bork2dException("device lost");
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            if (game.TickCount == 1)
            {
                renderer.PublishFatal(error);
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
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
    }

    [Fact]
    public void OffscreenDisposeDuringRunThrowsAndTheLoopContinuesUntilExit()
    {
        var renderer = new FakeRenderer { AutoReady = true };
        var factory = new FakeRendererFactory(renderer);
        var firstTick = new ManualResetEventSlim();
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () => firstTick.Set();

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory)
            .Build();

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
            Name = "Bork2d.Tests.OffscreenDisposeDuringRun",
        };
        thread.Start();

        try
        {
            Assert.True(
                firstTick.Wait(TimeSpan.FromSeconds(20)),
                "The offscreen loop did not start ticking.");
            Assert.Throws<InvalidOperationException>(() => app.Dispose());
            Assert.Null(Volatile.Read(ref runFailure));
            game.Engine!.Exit();
        }
        finally
        {
            Assert.True(
                thread.Join(TimeSpan.FromSeconds(20)),
                "The offscreen loop did not stop after IEngine.Exit().");
        }

        Assert.Null(runFailure);
        Assert.True(game.TickCount >= 1);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(renderer.Joined);
    }

    private static void AssertPlatformShutdown(FakeSdlApi sdl)
    {
        var window = Assert.Single(sdl.Windows);
        Assert.True(window.IsDisposed, "The SDL window must be destroyed before Run returns.");
        Assert.True(sdl.QuitCalled, "SDL video must be quit before Run returns.");
        Assert.True(sdl.Disposed, "The SDL adapter must be disposed before Run returns.");
    }
}
