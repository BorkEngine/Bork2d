using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for the M7 unordered capture pass: capture point values, world matrices,
/// layers (including invalid values), flips, null sprites, detach/attach, and unordered discovery.
/// </summary>
public sealed class SnapshotCaptureTests
{
    [Fact]
    public void EmptySceneCapturesNothingAndStampsTheTickValues()
    {
        var time = new FakeTime { Time = 2.5, DeltaTime = 0.5 };
        var engine = CreateEngine(time);

        var snapshot = Capture(engine, sequence: 7);

        Assert.Equal(7, snapshot.Sequence);
        Assert.Equal(2.5, snapshot.Time);
        Assert.Equal(0.5, snapshot.DeltaTime);
        Assert.Equal(0, snapshot.CameraCount);
        Assert.Equal(0, snapshot.SpriteCount);
    }

    [Fact]
    public void CameraPropertiesAreCopied()
    {
        var engine = CreateEngine();
        var camera = engine.CreateCamera(engine.Root);
        camera.ZIndex = 7;
        camera.Size = 12.5f;
        camera.Viewport = new Vector4(0.25f, 0.5f, 0.5f, 0.25f);
        camera.CullingMask = GameLayer.World | GameLayer.Player;
        camera.Clear = ClearKind.None;
        camera.ClearColor = Color.Red;

        var snapshot = Capture(engine);

        Assert.Equal(1, snapshot.CameraCount);
        CameraSnapshot captured = snapshot.Cameras[0];
        Assert.Equal(7, captured.ZIndex);
        Assert.Equal(12.5f, captured.Size);
        Assert.Equal(new Vector4(0.25f, 0.5f, 0.5f, 0.25f), captured.Viewport);
        Assert.Equal((uint)(GameLayer.World | GameLayer.Player), captured.CullingMask);
        Assert.Equal(ClearKind.None, captured.Clear);
        Assert.Equal(Color.Red, captured.ClearColor);
        Assert.Equal(Matrix3x2.Identity, captured.WorldMatrix);
    }

    [Fact]
    public void SpritePropertiesAreCopiedIncludingTheFrozenRenderDataAndFlips()
    {
        var engine = CreateEngine();
        var asset = new SpriteAsset();
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Sprite = asset;
        sprite.Layer = GameLayer.Player;
        sprite.Tint = Color.Yellow;
        sprite.ZIndex = -4;
        sprite.FlipX = true;
        sprite.FlipY = false;

        var snapshot = Capture(engine);

        Assert.Equal(1, snapshot.SpriteCount);
        SpriteSnapshot captured = snapshot.Sprites[0];
        Assert.Same(SpriteRenderData.Freeze(asset), captured.SpriteData);
        Assert.Equal((uint)GameLayer.Player, captured.Layer);
        Assert.Equal(Color.Yellow, captured.Tint);
        Assert.Equal(-4, captured.ZIndex);
        Assert.True(captured.FlipX);
        Assert.False(captured.FlipY);
        Assert.Equal(Matrix3x2.Identity, captured.WorldMatrix);
    }

    [Fact]
    public void MutatingTheAssetAfterAssignmentDoesNotChangeTheCapturedData()
    {
        var engine = CreateEngine();
        var asset = new SpriteAsset
        {
            Texture = "textures/player",
            Uvs =
            [
                new Vector2(0.1f, 0.2f),
                new Vector2(0.9f, 0.2f),
                new Vector2(0.9f, 0.8f),
                new Vector2(0.1f, 0.8f),
            ],
            Scale = new Vector2(2f, 3f),
            Pivot = new Vector2(0.25f, 0.75f),
            Topology = SpriteTopology.Quad,
            Indices = null,
        };
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Sprite = asset;
        SpriteRenderData frozen = Assert.IsType<SpriteRenderData>(Capture(engine).Sprites[0].SpriteData);

        asset.Uvs![0] = new Vector2(-1f, -1f);
        asset.Uvs = [new Vector2(0f, 0f)];
        asset.Scale = new Vector2(99f, 99f);
        asset.Pivot = new Vector2(1f, 0f);
        asset.Topology = SpriteTopology.Triangles;
        asset.Indices = [0, 1, 2];
        asset.Texture = "textures/other";

        SpriteRenderData after = Assert.IsType<SpriteRenderData>(Capture(engine).Sprites[0].SpriteData);

        Assert.Same(frozen, after);
        Assert.Equal("textures/player", after.Texture.Path);
        Assert.Equal(new Vector2(2f, 3f), after.Scale);
        Assert.Equal(new Vector2(0.25f, 0.75f), after.Pivot);
        Assert.Equal(SpriteTopology.Quad, after.Topology);
        Assert.Null(after.Indices);
        Assert.Equal(
            new[]
            {
                new Vector2(0.1f, 0.2f),
                new Vector2(0.9f, 0.2f),
                new Vector2(0.9f, 0.8f),
                new Vector2(0.1f, 0.8f),
            },
            after.Uvs);
    }

    [Fact]
    public void NullSpriteIsCapturedAndSkippedLaterByTheRenderer()
    {
        var engine = CreateEngine();
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Sprite = null;

        var snapshot = Capture(engine);

        Assert.Equal(1, snapshot.SpriteCount);
        Assert.Null(snapshot.Sprites[0].SpriteData);
    }

    [Fact]
    public void NestedTransformsProduceWorldMatricesThroughTheHierarchy()
    {
        var engine = CreateEngine();
        var outer = engine.CreateTransform(engine.Root);
        outer.LocalPosition = new Vector2(10f, 20f);
        var inner = engine.CreateTransform(outer);
        inner.LocalPosition = new Vector2(3f, 4f);
        var sprite = engine.CreateSpriteRender(inner);
        sprite.LocalPosition = new Vector2(1f, 2f);
        var camera = engine.CreateCamera(inner);
        camera.LocalPosition = new Vector2(-1f, -2f);

        var snapshot = Capture(engine);

        Assert.Equal(1, snapshot.SpriteCount);
        Assert.Equal(1, snapshot.CameraCount);
        Assert.Equal(new Vector2(14f, 26f), snapshot.Sprites[0].WorldMatrix.Translation);
        Assert.Equal(new Vector2(12f, 22f), snapshot.Cameras[0].WorldMatrix.Translation);
    }

    [Fact]
    public void WorldMatrixAppliesRotationAndScaleOfTheParentChain()
    {
        var engine = CreateEngine();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(100f, 50f);
        parent.LocalScale = new Vector2(2f, 2f);
        parent.LocalRotation = 90f;
        var sprite = engine.CreateSpriteRender(parent);
        sprite.LocalPosition = new Vector2(1f, 0f);

        var snapshot = Capture(engine);

        var expected = Matrix3x2.CreateTranslation(1f, 0f)
            * Matrix3x2.CreateScale(2f, 2f)
            * Matrix3x2.CreateRotation(MathF.PI / 2f)
            * Matrix3x2.CreateTranslation(100f, 50f);
        TestAssertions.Close(expected, snapshot.Sprites[0].WorldMatrix);
        TestAssertions.Close(new Vector2(100f, 52f), snapshot.Sprites[0].WorldMatrix.Translation);
    }

    [Fact]
    public void ReparentedNodeIsCapturedUnderItsNewParent()
    {
        var engine = CreateEngine();
        var first = engine.CreateTransform(engine.Root);
        first.LocalPosition = new Vector2(10f, 0f);
        var second = engine.CreateTransform(engine.Root);
        second.LocalPosition = new Vector2(20f, 0f);
        var sprite = engine.CreateSpriteRender(first);

        sprite.Parent = second;
        second.LocalPosition = new Vector2(5f, 0f);

        var snapshot = Capture(engine);

        Assert.Equal(1, snapshot.SpriteCount);
        TestAssertions.Close(new Vector2(-5f, 0f), snapshot.Sprites[0].WorldMatrix.Translation);
    }

    [Fact]
    public void DetachedNodesAreAbsentAndNodesAttachedBeforeCaptureArePresent()
    {
        var engine = CreateEngine();
        var sprite = engine.CreateSpriteRender(engine.Root);
        var camera = engine.CreateCamera(engine.Root);
        Assert.Equal(1, Capture(engine).SpriteCount);

        sprite.Parent = null;
        camera.Parent = null;
        var afterDetach = Capture(engine);
        Assert.Equal(0, afterDetach.SpriteCount);
        Assert.Equal(0, afterDetach.CameraCount);

        engine.CreateSpriteRender(engine.Root);
        var afterAttach = Capture(engine);
        Assert.Equal(1, afterAttach.SpriteCount);
        Assert.Equal(0, afterAttach.CameraCount);
    }

    [Fact]
    public void ADetachedSubtreeIsAbsentButItsInternalParentLinksStayValid()
    {
        var engine = CreateEngine();
        var subtree = engine.CreateTransform(engine.Root);
        engine.CreateSpriteRender(subtree);
        engine.CreateCamera(subtree);

        subtree.Parent = null;

        var snapshot = Capture(engine);
        Assert.Equal(0, snapshot.SpriteCount);
        Assert.Equal(0, snapshot.CameraCount);
    }

    [Fact]
    public void NonRenderableTransformsAreTraversedButNotCaptured()
    {
        var engine = CreateEngine();
        var outer = engine.CreateTransform(engine.Root);
        var inner = engine.CreateTransform(outer);
        engine.CreateTransform(inner);
        var sprite = engine.CreateSpriteRender(inner);

        var snapshot = Capture(engine);

        Assert.Equal(1, snapshot.SpriteCount);
        Assert.Equal(0, snapshot.CameraCount);
    }

    [Fact]
    public void InvalidLayersAreCapturedAsIs()
    {
        var engine = CreateEngine();
        var zero = engine.CreateSpriteRender(engine.Root);
        zero.Layer = GameLayer.None;
        var multiBit = engine.CreateSpriteRender(engine.Root);
        multiBit.Layer = (GameLayer)((int)GameLayer.World | (int)GameLayer.Player);
        var camera = engine.CreateCamera(engine.Root);
        camera.CullingMask = (GameLayer)0;

        var snapshot = Capture(engine);

        Assert.Equal(2, snapshot.SpriteCount);
        var layers = SortedLayers(snapshot);
        Assert.Equal(new[] { 0u, 3u }, layers);
        Assert.Equal(0u, snapshot.Cameras[0].CullingMask);
    }

    [Fact]
    public void Bit31SurvivesTheLayerFlattening()
    {
        var engine = new GameEngine<WideLayers>(new FakeTime(), new FakeWindow(), new FakeAssets());
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Layer = WideLayers.High;
        var camera = engine.CreateCamera(engine.Root);
        camera.CullingMask = WideLayers.High | WideLayers.Low;

        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence: 1);

        Assert.Equal(0x8000_0000u, snapshot.Sprites[0].Layer);
        Assert.Equal(0x8000_0001u, snapshot.Cameras[0].CullingMask);
    }

    [Fact]
    public void UnorderedCaptureContainsEveryRootedRenderableExactlyOnce()
    {
        var engine = CreateEngine();
        var branch = engine.CreateTransform(engine.Root);
        var nested = engine.CreateTransform(branch);
        for (var zIndex = 0; zIndex < 5; zIndex++)
        {
            var parent = zIndex % 2 == 0 ? engine.Root : (ITransform)nested;
            var sprite = engine.CreateSpriteRender(parent);
            sprite.ZIndex = zIndex;
        }

        var snapshot = Capture(engine);

        Assert.Equal(5, snapshot.SpriteCount);
        var zIndexes = new List<int>(snapshot.SpriteCount);
        foreach (SpriteSnapshot sprite in snapshot.Sprites)
        {
            zIndexes.Add(sprite.ZIndex);
        }

        zIndexes.Sort();
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, zIndexes);
    }

    [Fact]
    public void ReusingASnapshotResetsThePreviousCounts()
    {
        var engine = CreateEngine();
        var first = engine.CreateSpriteRender(engine.Root);
        engine.CreateSpriteRender(engine.Root);
        var shared = new FrameSnapshot();
        engine.CaptureSnapshot(shared, sequence: 1);
        Assert.Equal(2, shared.SpriteCount);

        first.Parent = null;
        engine.CaptureSnapshot(shared, sequence: 2);

        Assert.Equal(1, shared.SpriteCount);
        Assert.Equal(2, shared.Sequence);
    }

    private static GameEngine<GameLayer> CreateEngine(FakeTime? time = null)
        => new(time ?? new FakeTime(), new FakeWindow(), new FakeAssets());

    private static FrameSnapshot Capture(GameEngine<GameLayer> engine, long sequence = 1)
    {
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence);
        return snapshot;
    }

    private static uint[] SortedLayers(FrameSnapshot snapshot)
    {
        var layers = new uint[snapshot.SpriteCount];
        for (var index = 0; index < layers.Length; index++)
        {
            layers[index] = snapshot.Sprites[index].Layer;
        }

        Array.Sort(layers);
        return layers;
    }

    [Flags]
    private enum WideLayers
    {
        None = 0,
        Low = 1,
        High = int.MinValue,
    }
}
