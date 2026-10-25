using System.Numerics;
using Bork2d.Api;
using Bork2d.Common;
using Bork2d.Runtime;
using Xunit;

namespace Bork2d.Tests;

public sealed class Bork2dAppTests
{
    [Fact]
    public void RunCallsSetupTickAndTeardownExactlyOnce()
    {
        var game = new TestGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();

        app.Run();

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void RunCanBeCalledFromAnotherThread()
    {
        var game = new TestGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();

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
        using var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();
        app.Run();

        Assert.Throws<InvalidOperationException>(() => app.Run());

        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void RunAfterDisposeThrowsObjectDisposed()
    {
        var game = new TestGame<GameLayer>();
        var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();
        app.Dispose();

        Assert.Throws<ObjectDisposedException>(() => app.Run());

        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TeardownCount);
    }

    [Fact]
    public void DisposeBeforeRunIsSafe()
    {
        var game = new TestGame<GameLayer>();
        var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();

        app.Dispose();

        Assert.Equal(0, game.TeardownCount);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var game = new TestGame<GameLayer>();
        var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();
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
        var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();

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
        using var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();

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
        var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();

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
        using var app = new Bork2dAppBuilder<GameLayer>().WithGame(game).Build();

        var thrown = Assert.Throws<InvalidOperationException>(() => app.Run());

        Assert.Same(error, thrown);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void EngineWindowInputAndAssetsAreUnsupportedStubs()
    {
        var game = new ScriptedGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .Build();

        app.Run();

        var engine = game.Engine
            ?? throw new InvalidOperationException("Setup did not capture the engine.");

        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.Title; });
        Assert.Throws<NotSupportedException>(() => { engine.Window.Title = "title"; });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.Size; });
        Assert.Throws<NotSupportedException>(() => { engine.Window.Size = new Vector2(1f, 1f); });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.Fullscreen; });
        Assert.Throws<NotSupportedException>(() => { engine.Window.Fullscreen = true; });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Window.CursorVisible; });
        Assert.Throws<NotSupportedException>(() => { engine.Window.CursorVisible = false; });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Input; });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Assets.LoadSprite("sprite.png"); });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Assets.LoadData("data.bin"); });
        Assert.Throws<NotSupportedException>(() => { _ = engine.Assets.LoadAsset<object>("asset.bin"); });
    }

    [Fact]
    public void WithLoggingIsInvokedAtBuildWithoutAddingADefaultProvider()
    {
        var game = new TestGame<GameLayer>();
        var configured = false;

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithLogging(_ => configured = true)
            .Build();

        app.Run();

        Assert.Equal(1, game.TickCount);
        Assert.True(configured);
    }

    [Fact]
    public void DisposeDisposesTheLoggerFactoryAndNeverCallsTeardown()
    {
        var game = new ScriptedGame<GameLayer>();
        var engine = new Engine<GameLayer, ScriptedGame<GameLayer>>(
            game,
            new FakeTime(),
            new FakeWindow(),
            new FakeAssets());
        var loggerFactory = new FakeLoggerFactory();
        var options = new Bork2dAppOptions<GameLayer>
        {
            Game = game,
            GameSetCount = 1,
        };
        var app = new Bork2dApp(new AppRuntime<GameLayer>(engine, game, loggerFactory, options));

        app.Dispose();
        app.Dispose();

        Assert.Equal(1, loggerFactory.DisposeCount);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TeardownCount);
    }
}
