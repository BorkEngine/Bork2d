using System.Numerics;
using Bork2d.Api;
using Bork2d.Api.Assets;
using Bork2d.Api.Input;
using Bork2d.Api.Scene;
using Bork2d.Common.Scene;
using Bork2d.Common.Time;
using Bork2d.Runtime;
using Bork2d.Runtime.Hosting;
using Bork2d.Runtime.Input;
using Bork2d.Runtime.Window;
using Bork2d.Tests.Assets.Pack;
using Bork2d.Tests.TestDoubles;
using Xunit;

namespace Bork2d.Tests.Runtime;

public sealed class Bork2dAppTests
{
    [Fact]
    public void RunCallsSetupTickAndTeardownExactlyOnce()
    {
        var game = new TestGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        app.Run();

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void RunCanBeCalledFromAnotherThread()
    {
        var game = new TestGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        var thread = new Thread(app.Run);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The host run did not finish.");

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void SecondRunThrows()
    {
        var game = new TestGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();
        app.Run();

        Assert.Throws<InvalidOperationException>(() => app.Run());

        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void RunAfterDisposeThrowsObjectDisposed()
    {
        var game = new TestGame<GameLayer>();
        var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();
        app.Dispose();

        Assert.Throws<ObjectDisposedException>(() => app.Run());

        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TeardownCount);
    }

    [Fact]
    public void DisposeBeforeRunIsSafeAndDisposesTheUnstartedAssetService()
    {
        var game = new ScriptedGame<GameLayer>();
        var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();

        app.Dispose();

        Assert.Equal(0, game.TeardownCount);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var game = new TestGame<GameLayer>();
        var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();
        app.Run();

        app.Dispose();
        app.Dispose();

        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void TransformCreatedInSetupSurvivesRunAndIsDetachedByDispose()
    {
        ITransform? transform = null;
        var game = new ScriptedGame<GameLayer>
        {
            OnSetup = engine => transform = engine.CreateTransform(engine.Root),
        };
        var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        app.Run();

        var engine = game.Engine
            ?? throw new InvalidOperationException("Setup did not capture the engine.");
        var created = transform
            ?? throw new InvalidOperationException("Setup did not create the transform.");
        Assert.Same(engine.Root, created.Parent);

        app.Dispose();

        Assert.Null(created.Parent);
    }

    [Fact]
    public void SetupExceptionPropagatesWithoutTeardownAndClearsTheRoot()
    {
        var error = new InvalidOperationException("setup failed");
        ITransform? transform = null;
        var game = new ScriptedGame<GameLayer>
        {
            OnSetup = engine =>
            {
                transform = engine.CreateTransform(engine.Root);
                throw error;
            },
        };
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        var thrown = Assert.Throws<InvalidOperationException>(() => app.Run());

        Assert.Same(error, thrown);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);

        var created = transform
            ?? throw new InvalidOperationException("Setup did not create the transform.");
        Assert.Null(created.Parent);
        Assert.Throws<InvalidOperationException>(() => app.Run());
    }

    [Fact]
    public void TickExceptionPropagatesAfterTeardownAndClearsTheRoot()
    {
        var error = new InvalidOperationException("tick failed");
        ITransform? transform = null;
        var game = new ScriptedGame<GameLayer>
        {
            OnSetup = engine => transform = engine.CreateTransform(engine.Root),
            OnTick = () => throw error,
        };
        var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        var thrown = Assert.Throws<InvalidOperationException>(() => app.Run());

        Assert.Same(error, thrown);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);

        var created = transform
            ?? throw new InvalidOperationException("Setup did not create the transform.");
        Assert.Null(created.Parent);

        app.Dispose();
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void TeardownExceptionPropagates()
    {
        var error = new InvalidOperationException("teardown failed");
        var game = new ScriptedGame<GameLayer>
        {
            OnTeardown = () => throw error,
        };
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        var thrown = Assert.Throws<InvalidOperationException>(() => app.Run());

        Assert.Same(error, thrown);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void EngineWindowInputAndEmptyAssetFileSystemBehaveAsDocumented()
    {
        var game = new ScriptedGame<GameLayer>();
        IReadOnlyList<AssetInfo>? browse = null;
        bool? exists = null;
        Task<SpriteAsset>? spriteTask = null;
        Task<byte[]>? dataTask = null;
        Task<PackTestData>? assetTask = null;
        Exception? loadPackError = null;
        game.OnSetup = engine =>
        {
            browse = engine.Assets.Browse();
            exists = engine.Assets.Exists("data/missing");
            spriteTask = engine.Assets.LoadAsset<SpriteAsset>("sprites/missing");
            dataTask = engine.Assets.LoadData("data/missing");
            assetTask = engine.Assets.LoadAsset<PackTestData>("data/config");

            try
            {
                engine.Assets.LoadPack("overlays/missing.pck");
            }
            catch (Exception exception)
            {
                loadPackError = exception;
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        app.Run();

        var engine = game.Engine
            ?? throw new InvalidOperationException("Setup did not capture the engine.");

        // Window and input keep their M2 stubs in headless mode.
        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.Title; });
        Assert.Throws<NotSupportedException>(() => { engine.Window.Title = "title"; });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.Size; });
        Assert.Throws<NotSupportedException>(() => { engine.Window.Size = new Vector2(1f, 1f); });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.Fullscreen; });
        Assert.Throws<NotSupportedException>(() => { engine.Window.Fullscreen = true; });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.CursorVisible; });
        Assert.Throws<NotSupportedException>(() => { engine.Window.CursorVisible = false; });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.IsMinimized; });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Input; });

        // The real asset service is empty but present (D-M3-14).
        Assert.Empty(browse!);
        Assert.False(exists!.Value);

        var notFoundSprite = Assert.Throws<AssetException>(
            () => spriteTask!.GetAwaiter().GetResult());
        Assert.Equal(AssetErrorKind.NotFound, notFoundSprite.Kind);
        Assert.Equal("sprites/missing", notFoundSprite.AssetPath);

        var notFoundData = Assert.Throws<AssetException>(() => dataTask!.GetAwaiter().GetResult());
        Assert.Equal(AssetErrorKind.NotFound, notFoundData.Kind);
        Assert.Equal("data/missing", notFoundData.AssetPath);

        var notFoundAsset = Assert.Throws<AssetException>(() => assetTask!.GetAwaiter().GetResult());
        Assert.Equal(AssetErrorKind.NotFound, notFoundAsset.Kind);
        Assert.Equal("data/config", notFoundAsset.AssetPath);

        Assert.IsType<Bork2dException>(loadPackError);

        // The run stopped the service: reports throw, the getter does not (D-M3-14).
        Assert.NotNull(engine.Assets);
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.Browse(); });
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.Exists("data/missing"); });
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.LoadAsset<SpriteAsset>("sprites/missing"); });
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.LoadData("data/missing"); });
        Assert.Throws<ObjectDisposedException>(() => { engine.Assets.LoadPack("overlays/missing.pck"); });
    }

    [Fact]
    public void OneTickModesNeverCreateTheSdlPlatform()
    {
        var invoked = false;
        var game = new TestGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSdlApiFactory(() =>
            {
                invoked = true;
                throw new InvalidOperationException("Headless mode must not create SDL.");
            })
            .Build();

        app.Run();

        Assert.False(invoked);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void WithLoggingIsInvokedAtBuildWithoutAddingADefaultProvider()
    {
        var game = new TestGame<GameLayer>();
        var configured = false;

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithLogging(_ => configured = true)
            .Build();

        app.Run();

        Assert.Equal(1, game.TickCount);
        Assert.True(configured);
    }

    [Fact]
    public void DisposeDetachesTheWindowAndInputProxiesAndDisposesTheLoggerFactory()
    {
        var game = new ScriptedGame<GameLayer>();
        var window = new WindowProxy();
        var input = new InputProxy();
        var time = new StopwatchTime(0.25);
        var loggerFactory = new FakeLoggerFactory();
        var assetService = new Bork2d.Assets.Loading.AssetService(basePackPath: null, loggerFactory);
        var engine = new Engine<GameLayer>(time, window, assetService, input);
        var options = new Bork2dAppOptions<GameLayer>
        {
            Game = game,
            GameSetCount = 1,
        };
        var app = new Bork2dApp(
            new AppRuntime<GameLayer>(
                engine,
                game,
                time,
                loggerFactory,
                assetService,
                options,
                window,
                input));

        app.Dispose();
        app.Dispose();

        Assert.Equal(1, loggerFactory.DisposeCount);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TeardownCount);
        Assert.Throws<ObjectDisposedException>(() => { _ = window.Title; });
        Assert.Throws<ObjectDisposedException>(() => { _ = input.IsKeyDown(KeyCode.A); });

        // The unstarted service was disposed, never started: no loader thread, members throw.
        Assert.Null(assetService.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => { _ = assetService.Browse(); });
        Assert.Throws<ObjectDisposedException>(() => { _ = assetService.Exists("data/missing"); });
    }
}
