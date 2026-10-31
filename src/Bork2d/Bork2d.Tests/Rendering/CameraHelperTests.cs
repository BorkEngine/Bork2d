using System.Numerics;
using Bork2d.Engine.Platform;
using Bork2d.Engine.Scene;
using Bork2d.Runtime.Window;
using Bork2d.Tests.TestDoubles;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Tests for the public <c>ICamera.WorldToScreen</c>/<c>ScreenToWorld</c>/<c>WorldSize</c> additions
/// (M8 §6.9). They use the engine window's framebuffer size and require no GPU.
/// </summary>
public sealed class CameraHelperTests
{
    [Fact]
    public void WorldToScreenMapsTheCameraPositionToTheViewportCenter()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.LocalPosition = new Vector2(10f, 20f);
        camera.Size = 5f;

        Vector2 screen = camera.WorldToScreen(new Vector2(10f, 20f));

        Assert.Equal(400f, screen.X, 3);
        Assert.Equal(300f, screen.Y, 3);
    }

    [Fact]
    public void WorldSizeUsesTheRoundedViewportAspect()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;

        Vector2 worldSize = camera.WorldSize;

        Assert.Equal(2f * 5f * (800f / 600f), worldSize.X, 4);
        Assert.Equal(10f, worldSize.Y, 4);
    }

    [Fact]
    public void WorldSizeFollowsTheViewportRect()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;
        camera.Viewport = new Vector4(0.5f, 0f, 0.5f, 1f);

        Vector2 worldSize = camera.WorldSize;

        Assert.Equal(2f * 5f * (400f / 600f), worldSize.X, 4);
        Assert.Equal(10f, worldSize.Y, 4);
    }

    [Fact]
    public void WorldToScreenIncludesTheViewportOffset()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;
        camera.Viewport = new Vector4(0.5f, 0f, 0.5f, 1f);

        Vector2 center = camera.WorldToScreen(Vector2.Zero);

        Assert.Equal(600f, center.X, 3);
        Assert.Equal(300f, center.Y, 3);
    }

    [Fact]
    public void ScreenToWorldRoundTripsInsideAndOutsideTheViewport()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(1024f, 768f));
        var camera = engine.CreateCamera(engine.Root);
        camera.LocalPosition = new Vector2(-4f, 7f);
        camera.LocalRotation = 30f;
        camera.Size = 8f;
        camera.Viewport = new Vector4(0.1f, 0.2f, 0.4f, 0.6f);

        Vector2[] points =
        [
            Vector2.Zero,
            new Vector2(3f, -2f),
            new Vector2(-100f, 50f),
            new Vector2(1e4f, -1e4f),
        ];

        foreach (Vector2 world in points)
        {
            Vector2 screen = camera.WorldToScreen(world);
            Vector2 roundTrip = camera.ScreenToWorld(screen);
            Assert.Equal(world.X, roundTrip.X, 2);
            Assert.Equal(world.Y, roundTrip.Y, 2);
        }
    }

    [Fact]
    public void OutsideViewportScreenPointsExtrapolate()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;

        Vector2 screen = camera.WorldToScreen(new Vector2(100f, 100f));

        Assert.True(screen.X > 800f);
        Assert.True(screen.Y < 0f);
    }

    [Fact]
    public void ParentTranslationIsIncluded()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(100f, -50f);
        var camera = engine.CreateCamera(parent);
        camera.LocalPosition = new Vector2(-100f, 50f);
        camera.Size = 5f;

        Vector2 center = camera.WorldToScreen(Vector2.Zero);

        Assert.Equal(400f, center.X, 3);
        Assert.Equal(300f, center.Y, 3);
    }

    [Fact]
    public void RotationMatchesTheProjection()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.LocalRotation = 90f;
        camera.Size = 5f;

        // +Y above the camera maps to +X (the right side) after a 90-degree CCW camera rotation.
        Vector2 screen = camera.WorldToScreen(new Vector2(0f, 5f));

        Assert.Equal(700f, screen.X, 3);
        Assert.Equal(300f, screen.Y, 3);
    }

    [Fact]
    public void WindowSizeChangesWorldSize()
    {
        (GameEngine<GameLayer> engine, FakeWindow window) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;
        float before = camera.WorldSize.X;

        window.Size = new Vector2(600f, 800f);

        Assert.True(camera.WorldSize.X < before);
        Assert.Equal(2f * 5f * (600f / 800f), camera.WorldSize.X, 4);
    }

    [Fact]
    public void InvalidSizesThrowInvalidOperation()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);

        camera.Size = 0f;
        Assert.Throws<InvalidOperationException>(() => camera.WorldSize);
        Assert.Throws<InvalidOperationException>(() => camera.WorldToScreen(Vector2.Zero));
        Assert.Throws<InvalidOperationException>(() => camera.ScreenToWorld(Vector2.Zero));

        camera.Size = float.NaN;
        Assert.Throws<InvalidOperationException>(() => camera.WorldSize);

        camera.Size = 5f;
        camera.Viewport = new Vector4(0f, 0f, 0f, 1f);
        Assert.Throws<InvalidOperationException>(() => camera.WorldSize);

        camera.Viewport = new Vector4(0f, 0f, 0.0001f, 1f);
        Assert.Throws<InvalidOperationException>(() => camera.WorldSize);

        camera.Viewport = new Vector4(0f, 0f, 1f, 1f);
        camera.LocalScale = new Vector2(2f, 2f);
        Assert.Throws<InvalidOperationException>(() => camera.WorldSize);
    }

    [Fact]
    public void NonFiniteInputThrowsArgumentOutOfRange()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.WorldToScreen(new Vector2(float.NaN, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.WorldToScreen(new Vector2(0f, float.PositiveInfinity)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.ScreenToWorld(new Vector2(float.NaN, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.ScreenToWorld(new Vector2(0f, float.NegativeInfinity)));
    }

    [Fact]
    public void HeadlessWindowThrowsInvalidOperation()
    {
        var engine = new GameEngine<GameLayer>(
            new FakeTime(),
            new UnsupportedWindow(),
            new FakeAssets());
        var camera = engine.CreateCamera(engine.Root);

        InvalidOperationException worldSize = Assert.Throws<InvalidOperationException>(
            () => camera.WorldSize);
        Assert.Contains("framebuffer", worldSize.Message, StringComparison.OrdinalIgnoreCase);

        InvalidOperationException toScreen = Assert.Throws<InvalidOperationException>(
            () => camera.WorldToScreen(Vector2.Zero));
        Assert.Contains("headless", toScreen.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<InvalidOperationException>(() => camera.ScreenToWorld(Vector2.Zero));
    }

    [Fact]
    public void DisposedWindowPropagatesObjectDisposed()
    {
        var engine = new GameEngine<GameLayer>(
            new FakeTime(),
            new DisposedWindow(),
            new FakeAssets());
        var camera = engine.CreateCamera(engine.Root);

        Assert.Throws<ObjectDisposedException>(() => camera.WorldSize);
        Assert.Throws<ObjectDisposedException>(() => camera.WorldToScreen(Vector2.Zero));
        Assert.Throws<ObjectDisposedException>(() => camera.ScreenToWorld(Vector2.Zero));
    }

    [Fact]
    public void HelpersAreConsistentAcrossThreads()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;
        camera.LocalPosition = new Vector2(2f, 3f);

        Vector2 expected = camera.WorldToScreen(new Vector2(4f, 5f));
        var results = new Vector2[8];
        var threads = new Thread[results.Length];
        for (var index = 0; index < threads.Length; index++)
        {
            int captured = index;
            threads[index] = new Thread(
                () => results[captured] = camera.WorldToScreen(new Vector2(4f, 5f)));
            threads[index].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        foreach (Vector2 result in results)
        {
            Assert.Equal(expected.X, result.X, 3);
            Assert.Equal(expected.Y, result.Y, 3);
        }
    }

    private static (GameEngine<GameLayer> Engine, FakeWindow Window) CreateEngine(Vector2 size)
    {
        var window = new FakeWindow { Size = size };
        var engine = new GameEngine<GameLayer>(new FakeTime(), window, new FakeAssets());
        return (engine, window);
    }
}
