using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Scene;
using Bork2d.Runtime;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// An offscreen app exposes a functional <see cref="IWindow"/> reporting the fixed target extent,
/// so camera projection helpers work during <c>Setup</c>, <c>Tick</c>, and after the run without a
/// GPU.
/// </summary>
public sealed class OffscreenAppWindowTests
{
    [Fact]
    public void OffscreenWindowReportsTheTargetExtentAndCameraHelpersWork()
    {
        var game = new ScriptedGame<GameLayer> { ExitAfterFirstTick = true };
        ICamera<GameLayer>? camera = null;
        Vector2? setupSize = null;
        Vector2? tickSize = null;
        Vector2? teardownSize = null;
        Vector2? worldSize = null;
        Vector2? teardownWorldSize = null;
        Vector2? screen = null;
        Vector2? roundTrip = null;

        game.OnSetup = engine =>
        {
            setupSize = engine.Window.Size;
            camera = engine.CreateCamera(engine.Root);
            camera.Size = 5f;
            worldSize = camera.WorldSize;
            screen = camera.WorldToScreen(Vector2.Zero);
            roundTrip = camera.ScreenToWorld(screen.Value);
        };
        game.OnTick = () => tickSize = game.Engine!.Window.Size;
        game.OnTeardown = () =>
        {
            teardownSize = game.Engine!.Window.Size;
            teardownWorldSize = camera!.WorldSize;
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithOffscreen(320, 240)
            .WithoutAssets()
            .WithFakeGraphics()
            .Build();

        Exception? failure = AppTestSupport.RunCaptured(app);

        Assert.Null(failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(new Vector2(320f, 240f), setupSize);
        Assert.Equal(new Vector2(320f, 240f), tickSize);
        Assert.Equal(new Vector2(320f, 240f), teardownSize);

        var engine = game.Engine
            ?? throw new InvalidOperationException("Setup did not capture the engine.");
        Assert.Equal(new Vector2(320f, 240f), engine.Window.Size);

        Assert.True(float.IsFinite(worldSize!.Value.X));
        Assert.True(float.IsFinite(worldSize.Value.Y));
        Assert.Equal(2f * 5f * (320f / 240f), worldSize.Value.X, 4);
        Assert.Equal(10f, worldSize.Value.Y, 4);
        Assert.Equal(worldSize.Value, teardownWorldSize.GetValueOrDefault());

        Assert.True(float.IsFinite(screen!.Value.X));
        Assert.True(float.IsFinite(screen.Value.Y));
        Assert.Equal(160f, screen.Value.X, 3);
        Assert.Equal(120f, screen.Value.Y, 3);

        Assert.True(float.IsFinite(roundTrip!.Value.X));
        Assert.True(float.IsFinite(roundTrip.Value.Y));
        Assert.Equal(0f, roundTrip.Value.X, 3);
        Assert.Equal(0f, roundTrip.Value.Y, 3);

        // The facade stays functional after the run and the setter never resizes the target.
        engine.Window.Size = new Vector2(800f, 600f);
        Assert.Equal(new Vector2(320f, 240f), engine.Window.Size);
        Assert.False(engine.Window.IsMinimized);
    }
}
