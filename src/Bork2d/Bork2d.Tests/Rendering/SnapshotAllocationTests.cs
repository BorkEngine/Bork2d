using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Render.Handoff;
using Bork2d.Tests.TestDoubles;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Proves that a warm capture + publish + take + release loop allocates zero bytes for a fixed
/// entity count (Q6=Z1, M7 §6.9).
/// </summary>
public sealed class SnapshotAllocationTests
{
    private const int WarmupIterations = 500;
    private const int MeasuredIterations = 500;

    [Fact]
    public void WarmCapturePublishTakeReleaseAllocatesNothing()
    {
        var engine = CreateFixedScene();
        using var ring = new SnapshotRing();

        for (var iteration = 0; iteration < WarmupIterations; iteration++)
        {
            RunHandoffCycle(ring, engine, iteration + 1);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < MeasuredIterations; iteration++)
        {
            RunHandoffCycle(ring, engine, iteration + 1);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WarmCaptureOfAWideHierarchyAllocatesNothing()
    {
        var engine = new GameEngine<GameLayer>(new FakeTime(), new FakeWindow(), new FakeAssets());
        var branch = engine.CreateTransform(engine.Root);
        for (var index = 0; index < 256; index++)
        {
            var parent = index % 3 == 0 ? engine.Root : (ITransform)branch;
            var sprite = engine.CreateSpriteRender(parent);
            sprite.LocalPosition = new Vector2(index, -index);
            sprite.ZIndex = index;
        }

        var snapshot = new FrameSnapshot();
        for (var iteration = 0; iteration < WarmupIterations; iteration++)
        {
            engine.CaptureSnapshot(snapshot, sequence: iteration + 1);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < MeasuredIterations; iteration++)
        {
            engine.CaptureSnapshot(snapshot, sequence: iteration + 1);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(256, snapshot.SpriteCount);
    }

    [Fact]
    public void RepeatedCapturesKeepTheHighWaterCapacity()
    {
        var engine = CreateFixedScene();
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence: 1);
        var cameraCapacity = snapshot.CameraCapacity;
        var spriteCapacity = snapshot.SpriteCapacity;

        for (var iteration = 0; iteration < 100; iteration++)
        {
            engine.CaptureSnapshot(snapshot, sequence: iteration + 2);
        }

        Assert.Equal(cameraCapacity, snapshot.CameraCapacity);
        Assert.Equal(spriteCapacity, snapshot.SpriteCapacity);
        Assert.Equal(1, snapshot.CameraCount);
        Assert.Equal(8, snapshot.SpriteCount);
    }

    [Fact]
    public void WarmCaptureWithShaderUniformsAndGlobalsAllocatesNothing()
    {
        var engine = new GameEngine<GameLayer>(new FakeTime(), new FakeWindow(), new FakeAssets());
        var shader = new ShaderAsset
        {
            InstanceUniforms =
            [
                new ShaderUniform("tint", ShaderUniformType.Color, 0, Vector4.One),
                new ShaderUniform("intensity", ShaderUniformType.Float, 4, Vector4.Zero),
            ],
        };
        for (var index = 0; index < 8; index++)
        {
            var sprite = engine.CreateSpriteRender(engine.Root);
            sprite.Layer = GameLayer.World;
            sprite.Shader = shader;
            sprite.SetUniform("tint", new Color(0.25f, 0.5f, 0.75f, 1f));
            sprite.SetUniform("intensity", index);
            sprite.AdditionalTextures = ["textures/a", "textures/b"];
        }

        engine.SetGlobalUniform("scale", new Vector2(1f, 2f));
        for (var index = 0; index < 8; index++)
        {
            engine.SetGlobalUniform($"global{index}", index);
        }

        var snapshot = new FrameSnapshot();
        for (var iteration = 0; iteration < WarmupIterations; iteration++)
        {
            engine.CaptureSnapshot(snapshot, sequence: iteration + 1);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < MeasuredIterations; iteration++)
        {
            engine.CaptureSnapshot(snapshot, sequence: iteration + 1);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(8, snapshot.SpriteCount);
        Assert.Equal(9, snapshot.GlobalUniformCount);
    }

    [Fact]
    public void WarmCaptureOfLinesAllocatesNothing()
    {
        var engine = new GameEngine<GameLayer>(new FakeTime(), new FakeWindow(), new FakeAssets());
        var shader = new ShaderAsset
        {
            InstanceUniforms =
            [
                new ShaderUniform("intensity", ShaderUniformType.Float, 0, Vector4.Zero),
            ],
        };
        for (var index = 0; index < 8; index++)
        {
            ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
            line.Points =
            [
                new Vector2(index, 0f),
                new Vector2(index + 10f, 0f),
                new Vector2(index + 10f, 5f),
            ];
            line.Width = 1f + index;
            line.Color = Color.White;
            line.AntiAlias = index % 2 == 0;
            line.Layer = GameLayer.World;
            line.ZIndex = index;
            line.Shader = shader;
            line.SetUniform("intensity", index);
        }

        var snapshot = new FrameSnapshot();
        for (var iteration = 0; iteration < WarmupIterations; iteration++)
        {
            engine.CaptureSnapshot(snapshot, sequence: iteration + 1);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < MeasuredIterations; iteration++)
        {
            engine.CaptureSnapshot(snapshot, sequence: iteration + 1);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(8, snapshot.LineCount);
    }

    private static GameEngine<GameLayer> CreateFixedScene()
    {
        var engine = new GameEngine<GameLayer>(new FakeTime(), new FakeWindow(), new FakeAssets());
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 10f;
        var branch = engine.CreateTransform(engine.Root);
        branch.LocalPosition = new Vector2(5f, 5f);
        for (var index = 0; index < 8; index++)
        {
            var parent = index % 2 == 0 ? engine.Root : (ITransform)branch;
            var sprite = engine.CreateSpriteRender(parent);
            sprite.ZIndex = index;
            sprite.Layer = GameLayer.World;
        }

        return engine;
    }

    private static void RunHandoffCycle(SnapshotRing ring, GameEngine<GameLayer> engine, long sequence)
    {
        var slot = ring.Acquire();
        if (slot is null)
        {
            throw new InvalidOperationException("The ring stopped unexpectedly.");
        }

        engine.CaptureSnapshot(slot.Snapshot, sequence);
        ring.Publish(slot);

        var taken = ring.TakeLatest();
        if (taken is null)
        {
            throw new InvalidOperationException("The published snapshot was not ready.");
        }

        ring.Release(taken);
    }
}
