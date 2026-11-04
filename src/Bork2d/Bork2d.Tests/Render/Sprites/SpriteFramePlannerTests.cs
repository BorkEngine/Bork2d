using System.Numerics;
using Bork2d.Bake;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Render.Shaders;
using Bork2d.Render.Sprites;
using Bork2d.Render.Textures;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Deterministic tests for the M8 sprite frame planner: sorting, masks, validity, page resolution,
/// quad math, camera skips, conservative bounds culling, contiguous instance ranges, clears,
/// throttled diagnostics, and the allocation-free warm path (M8 §6.4–§6.8/§8.1).
/// </summary>
public sealed class SpriteFramePlannerTests
{
    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    [Fact]
    public void PlanSortsSpritesByLayerThenZAndDrawsOncePerNonEmptyCamera()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 3u);
        AddSprite(snapshot, Sprite("tex/a"), layer: 2, zIndex: 0, x: 30f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1, zIndex: 5, x: 10f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1, zIndex: 2, x: 20f);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.CameraCount);
        Assert.Equal(1, plan.CameraPlanCount);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(1, plan.NonEmptyCameras);
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(3, plan.InstanceCount);
        Assert.Equal(20f, fixture.Instances.Get(0).Transform1.X);
        Assert.Equal(10f, fixture.Instances.Get(1).Transform1.X);
        Assert.Equal(30f, fixture.Instances.Get(2).Transform1.X);
    }

    [Fact]
    public void CullingMasksFilterAndEmptyPreserveCamerasRecordNoPass()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 1u);
        AddCamera(snapshot, zIndex: 1, mask: 0u, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u);
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.CameraCount);
        Assert.Equal(2, plan.CameraPlanCount);
        Assert.Equal(1, plan.NonEmptyCameras);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.Cameras[1].InstanceCount);
        Assert.Equal(ClearKind.None, plan.Cameras[1].Clear);
        Assert.False(RecordsPass(plan.Cameras[1]));
    }

    [Fact]
    public void EmptyColorCameraRecordsAClearOnlyPassAndEmptyDepthCameraDoesNot()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.Color);
        AddCamera(snapshot, zIndex: 1, clear: ClearKind.Depth);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.CameraPlanCount);
        Assert.Equal(0, plan.DrawCalls);
        Assert.Equal(0, plan.InstanceCount);
        Assert.True(RecordsPass(plan.Cameras[0]));
        Assert.False(RecordsPass(plan.Cameras[1]));
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("ClearKind.Depth", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidCamerasAreSkippedIncludingTheirClearAndLogOncePerReason()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, size: 0f);
        AddCamera(snapshot, zIndex: 1, viewport: new Vector4(0f, 0f, 0f, 1f));
        AddCamera(snapshot, zIndex: 2, world: new Matrix3x2(0f, 0f, 0f, 0f, 0f, 0f));
        AddCamera(snapshot, zIndex: 3, world: Matrix3x2.CreateScale(2f));
        AddCamera(snapshot, zIndex: 4);

        SpriteFramePlan first = fixture.Plan(snapshot, 320, 240);
        SpriteFramePlan second = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(5, first.CameraCount);
        Assert.Equal(1, first.CameraPlanCount);
        Assert.Equal(1, second.CameraPlanCount);
        Assert.Equal(
            4,
            fixture.Logs.Entries.Count(
                entry => entry.Level == LogLevel.Warning
                    && entry.Message.Contains("Camera", StringComparison.Ordinal)));
    }

    [Fact]
    public void ZeroCamerasRecordNoPassButStillCountSkippedSprites()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        snapshot.AddSprite(new SpriteSnapshot(null, Matrix3x2.Identity, Color.White, 1u, 0, false, false));
        AddSprite(snapshot, Sprite("tex/a"), layer: 0u);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.CameraCount);
        Assert.Equal(0, plan.CameraPlanCount);
        Assert.Equal(0, plan.DrawCalls);
        Assert.Equal(0, plan.InstanceCount);
        Assert.Equal(2, plan.SkippedSprites);
        Assert.Equal(0, fixture.Instances.WriteCount);
    }

    [Fact]
    public void PendingSpritesAreSkippedUntilTheirPageIsReady()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/pending"), layer: 1u);

        SpriteFramePlan pending = fixture.Plan(snapshot, 320, 240);
        Assert.Equal(1, pending.SkippedSprites);
        Assert.Equal(0, pending.InstanceCount);

        fixture.MakeReadyPage("tex/pending", 8, 8);
        SpriteFramePlan ready = fixture.Plan(snapshot, 320, 240);
        Assert.Equal(0, ready.SkippedSprites);
        Assert.Equal(1, ready.InstanceCount);
        Assert.Equal(1, fixture.Instances.WriteCount);
    }

    [Fact]
    public void MalformedFieldsAreFatalEvenWhileThePageIsPending()
    {
        using var fixture = new PlanningFixture();

        // The page never loads: the malformed-field rule is unconditional and must run before the
        // pending skip.
        AssertFatal(fixture, Sprite("tex/pending-bad", scale: new Vector2(0f)), "Scale");
        AssertFatal(
            fixture,
            Sprite("tex/pending-bad", pivot: new Vector2(-0.1f, 0.5f)),
            "Pivot");
        SpriteAsset nullUvs = Sprite("tex/pending-bad");
        nullUvs.Uvs = null;
        AssertFatal(fixture, nullUvs, "Uvs");
        AssertFatal(fixture, Sprite("tex/pending-bad", indices: [0, 1, 2]), "Indices");

        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/pending-bad"), layer: 1u);

        SpriteFramePlan validPending = fixture.Plan(snapshot, 320, 240);
        Assert.Equal(1, validPending.SkippedSprites);
        Assert.Equal(0, validPending.InstanceCount);
    }

    [Fact]
    public void FailedAndAbsentTexturesDrawThePlaceholderWithTheAssetWorldScale()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeFailedPage("tex/failed", 32, 16);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/failed", scale: new Vector2(10f, 4f)), layer: 1u, zIndex: 0);
        AddSprite(snapshot, Sprite(texture: null, scale: new Vector2(10f, 4f)), layer: 1u, zIndex: 1);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.InstanceCount);
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(10f, fixture.Instances.Get(0).Transform1.Z, 3);
        Assert.Equal(4f, fixture.Instances.Get(0).Transform1.W, 3);
        Assert.Equal(10f, fixture.Instances.Get(1).Transform1.Z, 3);
        Assert.Equal(4f, fixture.Instances.Get(1).Transform1.W, 3);
        Assert.Equal(0f, fixture.Instances.Get(0).Misc.Z);
        Assert.Equal(0f, fixture.Instances.Get(1).Misc.Z);
    }

    [Fact]
    public void FlipsEncodeSignedSizeKeepUvsAndKeepFlagsZero()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        Vector2[] uvs =
        [
            new(0.25f, 0.1f),
            new(0.75f, 0.1f),
            new(0.75f, 0.9f),
            new(0.25f, 0.9f),
        ];
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a", scale: new Vector2(8f), uvs: uvs, pivot: new Vector2(0.25f, 0.75f)),
            layer: 1u,
            flipX: true,
            flipY: true);

        _ = fixture.Plan(snapshot, 320, 240);

        SpriteInstance instance = fixture.Instances.Get(0);
        Assert.Equal(-4f, instance.Transform1.Z, 5);
        Assert.Equal(-6.4f, instance.Transform1.W, 5);
        Assert.Equal(0.25f, instance.Misc.X);
        Assert.Equal(0.75f, instance.Misc.Y);
        Assert.Equal(0f, instance.Misc.W);
        Assert.Equal(0.25f, instance.Uv0.X);
        Assert.Equal(0.75f, instance.Uv0.Z);
        Assert.Equal(0.75f, instance.Uv1.X);
        Assert.Equal(0.25f, instance.Uv1.Z);
    }

    [Fact]
    public void OverlappingCamerasReceiveIndependentContiguousRanges()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 1f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 2f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 2, x: 3f);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(6, plan.InstanceCount);
        Assert.Equal(0, plan.Cameras[0].FirstInstance);
        Assert.Equal(3, plan.Cameras[0].InstanceCount);
        Assert.Equal(3, plan.Cameras[1].FirstInstance);
        Assert.Equal(3, plan.Cameras[1].InstanceCount);
        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(
                fixture.Instances.Get(index).Transform1.X,
                fixture.Instances.Get(index + 3).Transform1.X);
        }
    }

    [Fact]
    public void PerCameraVisibleRangesPreserveSortedOrderAndBoundsCulling()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b0101u, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, mask: 0b0010u, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 4u, zIndex: 2, x: 40f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u, zIndex: 1, x: 20f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 9, x: 10f, bounds: Box(100f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 3, x: 30f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 5f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 8u, zIndex: 0, x: 50f, bounds: Box(0f, 0f, 5f));

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.CameraPlanCount);
        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(1, plan.CulledSprites);
        Assert.Equal(4, plan.InstanceCount);
        Assert.Equal(0, plan.Cameras[0].FirstInstance);
        Assert.Equal(3, plan.Cameras[0].InstanceCount);
        Assert.Equal(3, plan.Cameras[1].FirstInstance);
        Assert.Equal(1, plan.Cameras[1].InstanceCount);
        Assert.Equal(new[] { 5f, 30f, 40f, 20f }, InstanceTranslations(fixture.Instances, plan.InstanceCount));
        Assert.Equal(4, fixture.Instances.WriteCount);
    }

    [Fact]
    public void ASpriteDrawnByMultipleCamerasCopiesTheSameCameraIndependentRecord()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        Vector2[] uvs =
        [
            new(0.25f, 0.1f),
            new(0.75f, 0.1f),
            new(0.75f, 0.9f),
            new(0.25f, 0.9f),
        ];
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(
            snapshot,
            zIndex: 0,
            mask: 1u,
            clear: ClearKind.None,
            viewport: new Vector4(0f, 0f, 0.5f, 1f));
        AddCamera(
            snapshot,
            zIndex: 1,
            mask: 1u,
            clear: ClearKind.None,
            viewport: new Vector4(0.5f, 0f, 0.5f, 1f));
        AddSprite(
            snapshot,
            Sprite("tex/a", scale: new Vector2(3f, 7f), uvs: uvs, pivot: new Vector2(0.25f, 0.75f)),
            layer: 1u,
            zIndex: 0,
            flipX: true,
            x: 4f);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.InstanceCount);
        Assert.Equal(1, plan.Cameras[0].InstanceCount);
        Assert.Equal(1, plan.Cameras[1].FirstInstance);
        Assert.Equal(1, plan.Cameras[1].InstanceCount);
        SpriteInstance first = fixture.Instances.Get(0);
        SpriteInstance second = fixture.Instances.Get(1);
        Assert.Equal(first.Transform0, second.Transform0);
        Assert.Equal(first.Transform1, second.Transform1);
        Assert.Equal(first.Uv0, second.Uv0);
        Assert.Equal(first.Uv1, second.Uv1);
        Assert.Equal(first.Tint, second.Tint);
        Assert.Equal(first.Misc, second.Misc);
        Assert.Equal(4f, first.Transform1.X);
        Assert.Equal(-1.5f, first.Transform1.Z, 5);
        Assert.Equal(5.6f, first.Transform1.W, 5);
        Assert.Equal(0.25f, first.Misc.X);
        Assert.Equal(0.75f, first.Misc.Y);
    }

    [Fact]
    public void LayerRangesIterateAscendingLayerAndZIndexAndSkipExcludedLayers()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b0000_1001u, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 8u, zIndex: 0, x: 80f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 7, x: 17f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u, zIndex: 0, x: 20f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: -2, x: 12f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 4u, zIndex: 5, x: 45f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 4, x: 14f);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(4, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
        Assert.Equal(new[] { 12f, 14f, 17f, 80f }, InstanceTranslations(fixture.Instances, plan.InstanceCount));
    }

    [Fact]
    public void FrameInstanceTotalBeyondIntMaxValueIsAFatalBork2dException()
    {
        long tooMany = (long)int.MaxValue + 1;

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteFramePlanner.ValidateInstanceCount(tooMany));

        Assert.Contains(tooMany.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(int.MaxValue.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("No instance is truncated", exception.Message, StringComparison.Ordinal);
        Assert.Equal(int.MaxValue, SpriteFramePlanner.ValidateInstanceCount(int.MaxValue));
        Assert.Equal(0, SpriteFramePlanner.ValidateInstanceCount(0));
    }

    [Fact]
    public void SharedVisibilityRuleAgreesWithThePlannerOnLayerAndBoundsCases()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b0011u, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, mask: 0b1001u, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 0f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 100f, bounds: Box(100f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u, zIndex: 2, x: 2f, bounds: Box(2f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 3, x: 1000f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 8u, zIndex: 4, x: 50f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 4u, zIndex: 5, x: 7f, bounds: Box(7f, 0f, 5f));

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        for (var cameraIndex = 0; cameraIndex < plan.CameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = plan.Cameras[cameraIndex];
            var actual = new List<float>();
            for (var index = 0; index < camera.InstanceCount; index++)
            {
                actual.Add(fixture.Instances.Get(camera.FirstInstance + index).Transform1.X);
            }

            var expected = new List<float>();
            for (var spriteIndex = 0; spriteIndex < snapshot.SpriteCount; spriteIndex++)
            {
                SpriteSnapshot sprite = snapshot.Sprites[spriteIndex];
                if (SpritePlanning.IsVisible(camera.CullingMask, camera.WorldBounds, sprite))
                {
                    expected.Add(sprite.WorldMatrix.M31);
                }
            }

            actual.Sort();
            expected.Sort();
            Assert.Equal(expected, actual);
        }

        var culled = 0;
        for (var spriteIndex = 0; spriteIndex < snapshot.SpriteCount; spriteIndex++)
        {
            SpriteSnapshot sprite = snapshot.Sprites[spriteIndex];
            var matchesAnyMask = false;
            var visibleToAnyCamera = false;
            for (var cameraIndex = 0; cameraIndex < plan.CameraPlanCount; cameraIndex++)
            {
                CameraDrawPlan camera = plan.Cameras[cameraIndex];
                if (!SpritePlanning.LayerMatches(camera.CullingMask, sprite.Layer))
                {
                    continue;
                }

                matchesAnyMask = true;
                visibleToAnyCamera |= SpritePlanning.IsVisible(
                    camera.CullingMask,
                    camera.WorldBounds,
                    sprite);
            }

            if (matchesAnyMask && !visibleToAnyCamera)
            {
                culled++;
            }
        }

        Assert.Equal(culled, plan.CulledSprites);
        Assert.Equal(0, plan.SkippedSprites);
    }

    [Fact]
    public void NonQuadTopologyIsSkippedWithOneDiagnosticPerAsset()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a", topology: SpriteTopology.TriangleStrip), layer: 1u);
        AddSprite(snapshot, Sprite("tex/a", topology: SpriteTopology.Triangles), layer: 1u);

        SpriteFramePlan first = fixture.Plan(snapshot, 320, 240);
        SpriteFramePlan second = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, first.SkippedSprites);
        Assert.Equal(2, second.SkippedSprites);
        Assert.Equal(0, first.InstanceCount);
        Assert.Equal(0, fixture.Assets.LoadAssetCount);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("topology", StringComparison.Ordinal)));
    }

    [Fact]
    public void MalformedSpriteFieldsAreFatalAndNameTheField()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);

        SpriteAsset nullUvs = Sprite("tex/a");
        nullUvs.Uvs = null;
        AssertFatal(fixture, nullUvs, "Uvs");
        AssertFatal(fixture, Sprite("tex/a", uvs: [new(0f, 0f)]), "Uvs");
        AssertFatal(
            fixture,
            Sprite(
                "tex/a",
                uvs: [new(float.NaN, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)]),
            "Uvs");
        AssertFatal(fixture, Sprite("tex/a", scale: new Vector2(0f)), "Scale");
        AssertFatal(fixture, Sprite("tex/a", scale: new Vector2(float.NaN)), "Scale");
        AssertFatal(fixture, Sprite("tex/a", pivot: new Vector2(1.1f, 0.5f)), "Pivot");
        AssertFatal(fixture, Sprite("tex/a", topology: (SpriteTopology)7), "Topology");
        AssertFatal(fixture, Sprite("tex/a", indices: [0, 1, 2]), "Indices");
    }

    [Fact]
    public void MutatingTheAssetAfterAssignmentKeepsTheCapturedDataAndPlannedQuad()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var engine = new GameEngine<GameLayer>(new FakeTime(), new FakeWindow(), new FakeAssets());
        var camera = engine.CreateCamera(engine.Root);
        camera.CullingMask = GameLayer.World;
        var sprite = engine.CreateSpriteRender(engine.Root);
        SpriteAsset asset = Sprite("tex/a", scale: new Vector2(2f, 3f));
        sprite.Sprite = asset;
        sprite.Layer = GameLayer.World;
        var snapshot = new FrameSnapshot { Sequence = 1 };
        engine.CaptureSnapshot(snapshot, 1);
        SpriteRenderData frozen = Assert.IsType<SpriteRenderData>(snapshot.Sprites[0].SpriteData);

        SpriteFramePlan before = fixture.Plan(snapshot, 320, 240);
        SpriteInstance first = fixture.Instances.Get(0);

        asset.Uvs![0] = new Vector2(-1f, -1f);
        asset.Indices = [0, 1, 2];
        asset.Scale = new Vector2(50f, 50f);
        asset.Pivot = new Vector2(1f, 0f);
        asset.Topology = SpriteTopology.Triangles;
        asset.Texture = "tex/other";

        var again = new FrameSnapshot { Sequence = 2 };
        engine.CaptureSnapshot(again, 2);
        SpriteFramePlan after = fixture.Plan(again, 320, 240);
        SpriteInstance second = fixture.Instances.Get(0);

        Assert.Equal(1, before.InstanceCount);
        Assert.Equal(1, after.InstanceCount);
        Assert.Same(frozen, again.Sprites[0].SpriteData);
        Assert.Equal(first.Transform1, second.Transform1);
        Assert.Equal(first.Uv0, second.Uv0);
        Assert.Equal(first.Uv1, second.Uv1);
        Assert.Equal(first.Misc, second.Misc);
        Assert.Equal(2f, second.Transform1.Z, 5);
        Assert.Equal(3f, second.Transform1.W, 5);
    }

    [Fact]
    public void WarmPlanAndBuildAllocateNothing()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, viewport: new Vector4(0f, 0f, 0.5f, 1f));
        AddCamera(snapshot, zIndex: 1, viewport: new Vector4(0.5f, 0f, 0.5f, 1f));
        for (var index = 0; index < 8; index++)
        {
            AddSprite(
                snapshot,
                Sprite("tex/a"),
                layer: (uint)((index % 2) + 1),
                zIndex: index,
                bounds: Box(0f, 0f, 5f));
        }

        _ = fixture.Plan(snapshot, 320, 240);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            total += fixture.Plan(snapshot, 320, 240).InstanceCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(200 * 8, total);
    }

    [Fact]
    public void WarmFiftyThousandSpritePlanAllocatesNothing()
    {
        const int spriteCount = 50_000;
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 4, 4);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        SpriteAsset asset = Sprite("tex/a", scale: new Vector2(8f));
        for (var index = 0; index < spriteCount; index++)
        {
            AddSprite(snapshot, asset, layer: 1u, zIndex: index, x: index * 0.01f);
        }

        SpriteFramePlan warm = fixture.Plan(snapshot, 1920, 1080);
        Assert.Equal(spriteCount, warm.InstanceCount);

        // Let tiered JIT finish and every pooled buffer reach its high-water mark before the
        // measurement; the first large plan can otherwise allocate on a cold test-host thread.
        for (var warmup = 0; warmup < 5; warmup++)
        {
            _ = fixture.Plan(snapshot, 1920, 1080);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var iteration = 0; iteration < 10; iteration++)
        {
            total += fixture.Plan(snapshot, 1920, 1080).InstanceCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(10 * spriteCount, total);
        Assert.True(
            fixture.Instances.InstanceCapacity >= spriteCount,
            "The warmed instance sink must hold the whole 50,000-sprite frame.");
    }

    [Fact]
    public void WarmMultiCameraPlanAllocatesNothing()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b0011u, viewport: new Vector4(0f, 0f, 0.5f, 1f));
        AddCamera(snapshot, zIndex: 1, mask: 0b0110u, viewport: new Vector4(0.5f, 0f, 0.5f, 1f));
        AddCamera(snapshot, zIndex: 2, mask: 0b1100u, clear: ClearKind.None);
        SpriteAsset asset = Sprite("tex/a", scale: new Vector2(2f));
        for (var index = 0; index < 1000; index++)
        {
            Bounds? bounds = index % 3 == 0 ? null : Box(index * 0.01f, 0f, 5f);
            AddSprite(
                snapshot,
                asset,
                layer: (uint)(1 << (index % 4)),
                zIndex: index,
                x: index * 0.01f,
                bounds: bounds);
        }

        int expected = fixture.Plan(snapshot, 1920, 1080).InstanceCount;
        Assert.True(expected > 0);
        for (var warmup = 0; warmup < 5; warmup++)
        {
            _ = fixture.Plan(snapshot, 1920, 1080);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            total += fixture.Plan(snapshot, 1920, 1080).InstanceCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(200 * expected, total);
    }

    [Fact]
    public void BoundsCullingSkipsSpritesOutsideTheCameraAndKeepsUnknownBounds()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 0f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 100f, bounds: Box(100f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 2, x: 13f, bounds: Box(13f, 0f, 5f));

        // No bounds means "unknown": a far sprite with empty bounds stays conservative and draws.
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 3, x: 1000f);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.CameraCount);
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(1, plan.CulledSprites);
        Assert.Equal(3, plan.InstanceCount);
        Assert.Equal(3, fixture.Instances.WriteCount);
        Assert.Equal(3, fixture.Instances.LastRequiredInstances);
    }

    [Fact]
    public void LayerFilteringRunsBeforeBoundsCulling()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 1u);
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u, x: 100f, bounds: Box(100f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 100f, bounds: Box(100f, 0f, 5f));

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        // The layer-mismatched sprite was never a bounds candidate, so only the layer-matched
        // offscreen sprite counts as culled.
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(1, plan.CulledSprites);
        Assert.Equal(0, plan.InstanceCount);
        Assert.Equal(0, fixture.Instances.WriteCount);
    }

    [Fact]
    public void ASpriteCulledByOneCameraIsStillDrawnByAnother()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.None);
        AddCamera(
            snapshot,
            zIndex: 1,
            clear: ClearKind.None,
            world: Matrix3x2.CreateTranslation(100f, 0f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 100f, bounds: Box(100f, 0f, 5f));

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
        Assert.Equal(0, plan.Cameras[0].InstanceCount);
        Assert.Equal(1, plan.Cameras[1].InstanceCount);
    }

    [Fact]
    public void CulledSpritesCountsEachSpriteOnceAcrossOverlappingCameras()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 0f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 100f, bounds: Box(100f, 0f, 5f));

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.InstanceCount);
        Assert.Equal(1, plan.CulledSprites);
        Assert.Equal(2, fixture.Instances.WriteCount);
    }

    [Fact]
    public void MalformedFieldsRemainFatalBeforeBoundsCulling()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a", scale: Vector2.Zero),
            layer: 1u,
            x: 100f,
            bounds: Box(100f, 0f, 5f));

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => fixture.Plan(snapshot, 320, 240));
        Assert.Contains("Scale", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteCameraBoundsNeverCull()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, size: float.MaxValue);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 1000f, bounds: Box(1000f, 0f, 5f));

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.CameraPlanCount);
        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
    }

    [Fact]
    public void ProductionBoundsKeepRotatedCameraSpritesVisible()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(320f, 240f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 10f;
        camera.LocalRotation = 30f;
        camera.CullingMask = GameLayer.World;
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Sprite = Sprite("tex/a", scale: new Vector2(4f));
        sprite.Layer = GameLayer.World;

        // The view-space top-right corner (13, 9.5) is inside the rotated view. Unrotated camera
        // bounds would end at y = 10 and wrongly cull it.
        float cosine = MathF.Cos(MathF.PI / 6f);
        float sine = MathF.Sin(MathF.PI / 6f);
        var viewCorner = new Vector2(13f, 9.5f);
        sprite.LocalPosition = new Vector2(
            (viewCorner.X * cosine) - (viewCorner.Y * sine),
            (viewCorner.X * sine) + (viewCorner.Y * cosine));

        FrameSnapshot snapshot = CaptureEngine(engine);
        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
    }

    [Fact]
    public void ProductionBoundsKeepMirroredPivotedSpritesVisible()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(320f, 240f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 10f;
        camera.CullingMask = GameLayer.World;
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Sprite = Sprite("tex/a", scale: new Vector2(6f), pivot: new Vector2(0.1f, 0.1f));
        sprite.Layer = GameLayer.World;
        sprite.FlipX = true;
        sprite.FlipY = true;
        sprite.LocalPosition = new Vector2(14f, -14f);

        FrameSnapshot snapshot = CaptureEngine(engine);
        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        // The mirrored pivot places the quad at x [8.6, 14.6], y [-14.6, -8.6]; dropping the flip
        // sign would put it at x [13.4, 19.4], y [-19.4, -13.4] and wrongly cull it.
        Assert.False(snapshot.Sprites[0].Bounds.IsEmpty);
        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
    }

    [Fact]
    public void ProductionBoundsKeepShearedSpritesVisible()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(320f, 240f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 10f;
        camera.CullingMask = GameLayer.World;
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalScale = new Vector2(2f, 1f);
        var sprite = engine.CreateSpriteRender(parent);
        sprite.Sprite = Sprite("tex/a", scale: new Vector2(10f, 4f));
        sprite.Layer = GameLayer.World;
        sprite.LocalPosition = new Vector2(11f, 0f);
        sprite.LocalRotation = 30f;

        FrameSnapshot snapshot = CaptureEngine(engine);
        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        // The sheared quad reaches x = 11.34; the un-sheared quad would start at x = 17 and be
        // wrongly culled against the camera's x extent of 13.33.
        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
    }

    [Fact]
    public void ProductionBoundsKeepNonSquareSpritesVisible()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(320f, 240f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 10f;
        camera.CullingMask = GameLayer.World;
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Sprite = Sprite("tex/a", scale: new Vector2(2f, 8f));
        sprite.Layer = GameLayer.World;
        sprite.LocalPosition = new Vector2(13f, 0f);

        FrameSnapshot snapshot = CaptureEngine(engine);
        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
    }

    [Fact]
    public void PlanWithNoRunsWritesNoGlobalBlock()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot
        {
            Sequence = 4,
            Time = 12.5,
            DeltaTime = 0.016,
        };
        AddCamera(snapshot, zIndex: 0);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.DrawCalls);
        Assert.Equal(0, plan.RunCount);
        Assert.Equal(1, fixture.Globals.EnsureCapacityCallCount);
        Assert.Equal(0, fixture.Globals.LastRequiredBlocks);
        Assert.Equal(0, fixture.Globals.WriteCount);
    }

    [Fact]
    public void PlanWritesOneGlobalBlockPerRunWithTheFrameTimeAndShaderDefaults()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader(
                "materials/globals",
                globals:
                [
                    new ShaderUniform(
                        "gColor",
                        ShaderUniformType.Color,
                        0,
                        new Vector4(0.1f, 0.2f, 0.3f, 0.4f)),
                    new ShaderUniform(
                        "gScale",
                        ShaderUniformType.Float,
                        4,
                        new Vector4(2f, 0f, 0f, 0f)),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot
        {
            Sequence = 4,
            Time = 12.5,
            DeltaTime = 0.016,
        };
        snapshot.AddGlobalUniform(
            new GlobalUniformSnapshot(
                "gColor",
                ShaderUniformType.Color,
                new Vector4(0.9f, 0.8f, 0.7f, 0.6f)));
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, shader: metadata);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.RunCount);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(1, fixture.Globals.EnsureCapacityCallCount);
        Assert.Equal(1, fixture.Globals.LastRequiredBlocks);
        Assert.Equal(1, fixture.Globals.WriteCount);
        Assert.Equal(0, fixture.Globals.LastWrittenIndex);

        GlobalUniformBlock block = fixture.Globals.Get(0);
        Assert.Equal(new Vector4(12.5f, 0f, 0f, 0f), block.Time);
        Assert.Equal(new Vector4(0.9f, 0.8f, 0.7f, 0.6f), block.Global0);
        Assert.Equal(new Vector4(2f, 0f, 0f, 0f), block.Global1);
        Assert.Equal(Vector4.Zero, block.Global2);
        Assert.Equal(0u, plan.Runs[0].GlobalBlockOffset);
        Assert.Equal(144, fixture.Globals.BlockStride);
        Assert.Equal(144, GlobalUniformBlock.SizeInBytes);
    }

    [Fact]
    public void EachRunOwnsItsGlobalBlockAndDynamicOffset()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData first = fixture.Shaders.Register(
            CustomShader(
                "materials/first",
                globals:
                [
                    new ShaderUniform(
                        "gA",
                        ShaderUniformType.Vector2,
                        0,
                        new Vector4(3f, 4f, 0f, 0f)),
                ]),
            ShaderBindingState.Ready);
        ShaderRenderData second = fixture.Shaders.Register(
            CustomShader(
                "materials/second",
                globals:
                [
                    new ShaderUniform(
                        "gB",
                        ShaderUniformType.Float,
                        0,
                        new Vector4(7f, 0f, 0f, 0f)),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1, Time = 2f };
        snapshot.AddGlobalUniform(
            new GlobalUniformSnapshot("gA", ShaderUniformType.Vector2, new Vector4(5f, 6f, 0f, 0f)));
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, shader: first);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, shader: second);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.RunCount);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(2, fixture.Globals.WriteCount);
        Assert.Equal(0u, plan.Runs[0].GlobalBlockOffset);
        Assert.Equal((uint)fixture.Globals.BlockStride, plan.Runs[1].GlobalBlockOffset);
        Assert.Equal(new Vector4(5f, 6f, 0f, 0f), fixture.Globals.Get(0).Global0);
        Assert.Equal(new Vector4(7f, 0f, 0f, 0f), fixture.Globals.Get(1).Global0);
        Assert.Equal(new Vector4(2f, 0f, 0f, 0f), fixture.Globals.Get(0).Time);
        Assert.Equal(new Vector4(2f, 0f, 0f, 0f), fixture.Globals.Get(1).Time);
    }

    [Fact]
    public void PendingCustomShaderSkipsThenDrawsTheFrameAfterItBecomesReady()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderAsset asset = CustomShader("materials/pending");
        ShaderRenderData metadata = fixture.Shaders.Register(asset, ShaderBindingState.Pending);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, shader: metadata);

        SpriteFramePlan pending = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, pending.SkippedSprites);
        Assert.Equal(0, pending.InstanceCount);
        Assert.Equal(0, pending.DrawCalls);
        Assert.Equal(0, fixture.Instances.WriteCount);

        _ = fixture.Shaders.Register(asset, ShaderBindingState.Ready);
        SpriteFramePlan ready = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, ready.SkippedSprites);
        Assert.Equal(1, ready.InstanceCount);
        Assert.Equal(1, ready.DrawCalls);
        Assert.NotEqual(fixture.Shaders.DefaultEntryId, ready.Runs[0].ShaderEntryId);
    }

    [Fact]
    public void FailedCustomShaderDrawsWithTheDefaultEntryAndIsNotSkipped()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader("materials/failed"),
            ShaderBindingState.Failed);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, shader: metadata);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(fixture.Shaders.DefaultEntryId, plan.Runs[0].ShaderEntryId);
        Assert.Equal(Vector4.Zero, fixture.Instances.Get(0).InstanceParams0);
    }

    [Fact]
    public void AlternatingShadersFormContiguousRunsAndNeverReorderSprites()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData first = fixture.Shaders.Register(
            CustomShader("materials/first"),
            ShaderBindingState.Ready);
        ShaderRenderData second = fixture.Shaders.Register(
            CustomShader("materials/second"),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 10f, shader: first);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 20f, shader: first);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 2, x: 30f, shader: second);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 3, x: 40f, shader: first);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(4, plan.InstanceCount);
        Assert.Equal(3, plan.RunCount);
        Assert.Equal(3, plan.DrawCalls);
        Assert.Equal(1, plan.NonEmptyCameras);
        Assert.Equal(0, plan.Cameras[0].FirstRun);
        Assert.Equal(3, plan.Cameras[0].RunCount);
        Assert.Equal(new[] { 10f, 20f, 30f, 40f }, InstanceTranslations(fixture.Instances, 4));

        Assert.Equal(0, plan.Runs[0].FirstInstance);
        Assert.Equal(2, plan.Runs[0].InstanceCount);
        Assert.Equal(fixture.Shaders.EntryIdFor(first), plan.Runs[0].ShaderEntryId);
        Assert.Equal(2, plan.Runs[1].FirstInstance);
        Assert.Equal(1, plan.Runs[1].InstanceCount);
        Assert.Equal(fixture.Shaders.EntryIdFor(second), plan.Runs[1].ShaderEntryId);
        Assert.Equal(3, plan.Runs[2].FirstInstance);
        Assert.Equal(1, plan.Runs[2].InstanceCount);
        Assert.Equal(fixture.Shaders.EntryIdFor(first), plan.Runs[2].ShaderEntryId);
    }

    [Fact]
    public void PerCameraRunsAreCountedIndependentlyAcrossOverlappingCameras()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData first = fixture.Shaders.Register(
            CustomShader("materials/first"),
            ShaderBindingState.Ready);
        ShaderRenderData second = fixture.Shaders.Register(
            CustomShader("materials/second"),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, shader: first);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, shader: second);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(4, plan.DrawCalls);
        Assert.Equal(4, plan.InstanceCount);
        Assert.Equal(0, plan.Cameras[0].FirstRun);
        Assert.Equal(2, plan.Cameras[0].RunCount);
        Assert.Equal(2, plan.Cameras[1].FirstRun);
        Assert.Equal(2, plan.Cameras[1].RunCount);
        Assert.Equal(plan.Runs[0].ShaderEntryId, plan.Runs[2].ShaderEntryId);
        Assert.Equal(plan.Runs[1].ShaderEntryId, plan.Runs[3].ShaderEntryId);
    }

    [Fact]
    public void DefaultOnlySceneIsExactlyOneRunPerCamera()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 2);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.RunCount);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(1, plan.Cameras[0].RunCount);
        Assert.Equal(1, plan.Cameras[1].RunCount);
        Assert.Equal(3, plan.Runs[0].InstanceCount);
        Assert.Equal(3, plan.Runs[1].InstanceCount);
        Assert.Equal(fixture.Shaders.DefaultEntryId, plan.Runs[0].ShaderEntryId);
        Assert.Equal(fixture.Shaders.DefaultEntryId, plan.Runs[1].ShaderEntryId);
    }

    [Fact]
    public void ExtraTexturesResolveDeclaredSlotsAndUseThePlaceholderForFailedOrAbsentRefs()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        fixture.MakeReadyPage("tex/extra", 8, 8);
        fixture.MakeFailedPage("tex/bad", 8, 8);
        int extraIndex = fixture.Store.EnsurePage("tex/extra").Index;
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader(
                "materials/textures",
                slots:
                [
                    new ShaderTextureSlot("t0"),
                    new ShaderTextureSlot("t1"),
                    new ShaderTextureSlot("t2"),
                    new ShaderTextureSlot("t3"),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a"),
            layer: 1u,
            shader: metadata,
            extraTexture0: "tex/extra",
            extraTexture1: "tex/bad",
            extraTexture3: "tex/extra",
            additionalTextureCount: 4);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.InstanceCount);
        SpriteInstance instance = fixture.Instances.Get(0);
        Assert.Equal(extraIndex, instance.ExtraTextureIndices.X);
        Assert.Equal(0f, instance.ExtraTextureIndices.Y);
        Assert.Equal(0f, instance.ExtraTextureIndices.Z);
        Assert.Equal(extraIndex, instance.ExtraTextureIndices.W);
    }

    [Fact]
    public void PendingExtraTextureSkipsTheWholeSpriteUntilThePageIsReady()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader("materials/textures", slots: [new ShaderTextureSlot("t0")]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a"),
            layer: 1u,
            shader: metadata,
            extraTexture0: "tex/pending-extra",
            additionalTextureCount: 1);

        SpriteFramePlan pending = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, pending.SkippedSprites);
        Assert.Equal(0, pending.InstanceCount);

        fixture.MakeReadyPage("tex/pending-extra", 8, 8);
        SpriteFramePlan ready = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, ready.SkippedSprites);
        Assert.Equal(1, ready.InstanceCount);
        Assert.NotEqual(0f, fixture.Instances.Get(0).ExtraTextureIndices.X);
    }

    [Fact]
    public void AdditionalTexturesBeyondFourAreIgnoredWithOneWarning()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        fixture.MakeReadyPage("tex/extra", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader(
                "materials/textures",
                slots:
                [
                    new ShaderTextureSlot("t0"),
                    new ShaderTextureSlot("t1"),
                    new ShaderTextureSlot("t2"),
                    new ShaderTextureSlot("t3"),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a"),
            layer: 1u,
            shader: metadata,
            extraTexture0: "tex/extra",
            extraTexture1: "tex/extra",
            extraTexture2: "tex/extra",
            extraTexture3: "tex/extra",
            additionalTextureCount: 5);

        SpriteFramePlan first = fixture.Plan(snapshot, 320, 240);
        SpriteFramePlan second = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, first.InstanceCount);
        Assert.Equal(1, second.InstanceCount);
        Assert.Equal(0, first.SkippedSprites);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("at most", StringComparison.Ordinal)));
    }

    [Fact]
    public void ExtraTexturesBeyondTheDeclaredSlotCountAreNotLoaded()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        fixture.MakeReadyPage("tex/used", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader("materials/textures", slots: [new ShaderTextureSlot("t0")]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a"),
            layer: 1u,
            shader: metadata,
            extraTexture0: "tex/used",
            extraTexture1: "tex/unused",
            additionalTextureCount: 2);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(1, plan.InstanceCount);
        Assert.DoesNotContain("tex/unused", fixture.Assets.LoadAssetPaths);
        Assert.Equal(0f, fixture.Instances.Get(0).ExtraTextureIndices.Y);
    }

    [Fact]
    public void DefaultEntryDeclarationsSeedInstanceUniformsAndTextureSlots()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        fixture.MakeReadyPage("tex/extra", 8, 8);
        int extraIndex = fixture.Store.EnsurePage("tex/extra").Index;
        fixture.Shaders.SetDefaultMetadata(
            CustomShader(
                "shader/sprite-spv",
                instance:
                [
                    new ShaderUniform(
                        "uDefault",
                        ShaderUniformType.Vector4,
                        0,
                        new Vector4(2f, 3f, 4f, 5f)),
                ],
                slots: [new ShaderTextureSlot("t0")]));
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a"),
            layer: 1u,
            uniform0: new Vector4(9f),
            extraTexture0: "tex/extra",
            additionalTextureCount: 1);

        SpriteFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.InstanceCount);
        Assert.Null(snapshot.Sprites[0].ShaderData);
        SpriteInstance instance = fixture.Instances.Get(0);
        Assert.Equal(new Vector4(2f, 3f, 4f, 5f), instance.InstanceParams0);
        Assert.Equal(extraIndex, instance.ExtraTextureIndices.X);
    }

    [Fact]
    public void CustomInstanceUniformLanesAreCopiedFromTheSnapshot()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader(
                "materials/uniforms",
                instance:
                [
                    new ShaderUniform("u0", ShaderUniformType.Vector4, 0, Vector4.Zero),
                    new ShaderUniform("u1", ShaderUniformType.Float, 4, Vector4.Zero),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a"),
            layer: 1u,
            shader: metadata,
            uniform0: new Vector4(1f, 2f, 3f, 4f),
            uniform1: new Vector4(5f, 0f, 0f, 0f),
            uniform2: new Vector4(6f, 0f, 0f, 0f),
            uniform3: new Vector4(7f, 0f, 0f, 0f));

        _ = fixture.Plan(snapshot, 320, 240);

        SpriteInstance instance = fixture.Instances.Get(0);
        Assert.Equal(new Vector4(1f, 2f, 3f, 4f), instance.InstanceParams0);
        Assert.Equal(new Vector4(5f, 0f, 0f, 0f), instance.InstanceParams1);
        Assert.Equal(new Vector4(6f, 0f, 0f, 0f), instance.InstanceParams2);
        Assert.Equal(new Vector4(7f, 0f, 0f, 0f), instance.InstanceParams3);
    }

    [Fact]
    public void GlobalTypeMismatchFallsBackToTheDefaultEntryWithOneWarning()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader(
                "materials/global-mismatch",
                globals:
                [
                    new ShaderUniform(
                        "gValue",
                        ShaderUniformType.Float,
                        0,
                        new Vector4(3f, 0f, 0f, 0f)),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        snapshot.AddGlobalUniform(
            new GlobalUniformSnapshot("gValue", ShaderUniformType.Vector2, new Vector4(1f, 2f, 0f, 0f)));
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, shader: metadata);

        SpriteFramePlan first = fixture.Plan(snapshot, 320, 240);
        SpriteFramePlan second = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, first.InstanceCount);
        Assert.Equal(fixture.Shaders.DefaultEntryId, first.Runs[0].ShaderEntryId);
        Assert.Equal(0f, fixture.Globals.Get(0).Global0.X);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("differs", StringComparison.Ordinal)));
        Assert.Equal(1, second.InstanceCount);
    }

    private static Bounds Box(float x, float y, float halfExtent)
        => Bounds.FromMinMax(
            new Vector2(x - halfExtent, y - halfExtent),
            new Vector2(x + halfExtent, y + halfExtent));

    private static ShaderAsset CustomShader(
        string spirVPath,
        ShaderUniform[]? instance = null,
        ShaderUniform[]? globals = null,
        ShaderTextureSlot[]? slots = null,
        uint layoutVersion = ShaderLayout.CurrentVersion,
        string entryPoint = "main")
        => new()
        {
            SpirV = spirVPath,
            LayoutVersion = layoutVersion,
            EntryPoint = entryPoint,
            InstanceUniforms = instance ?? [],
            GlobalUniforms = globals ?? [],
            TextureSlots = slots ?? [],
        };

    private static float[] InstanceTranslations(FakeInstanceBuffer instances, int count)
    {
        var translations = new float[count];
        for (var index = 0; index < count; index++)
        {
            translations[index] = instances.Get(index).Transform1.X;
        }

        return translations;
    }

    private static (GameEngine<GameLayer> Engine, FakeWindow Window) CreateEngine(Vector2 size)
    {
        var window = new FakeWindow { Size = size };
        return (new GameEngine<GameLayer>(new FakeTime(), window, new FakeAssets()), window);
    }

    private static FrameSnapshot CaptureEngine(GameEngine<GameLayer> engine)
    {
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, 1);
        return snapshot;
    }

    private static void AssertFatal(PlanningFixture fixture, SpriteAsset asset, string field)
    {
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, asset, layer: 1u);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => fixture.Plan(snapshot, 320, 240));
        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
    }

    private static bool RecordsPass(in CameraDrawPlan camera)
        => camera.InstanceCount > 0 || camera.Clear == ClearKind.Color;

    private static void AddCamera(
        FrameSnapshot snapshot,
        int zIndex,
        float size = 10f,
        Vector4? viewport = null,
        uint mask = 1u,
        ClearKind clear = ClearKind.Color,
        Matrix3x2? world = null)
        => snapshot.AddCamera(
            new CameraSnapshot(
                zIndex,
                world ?? Matrix3x2.Identity,
                size,
                viewport ?? new Vector4(0f, 0f, 1f, 1f),
                mask,
                clear,
                new Color(0f, 0f, 0f, 1f)));

    private static void AddSprite(
        FrameSnapshot snapshot,
        SpriteAsset asset,
        uint layer,
        int zIndex = 0,
        bool flipX = false,
        bool flipY = false,
        float x = 0f,
        Bounds? bounds = null,
        ShaderRenderData? shader = null,
        Vector4? uniform0 = null,
        Vector4? uniform1 = null,
        Vector4? uniform2 = null,
        Vector4? uniform3 = null,
        AssetRef<TextureDescriptor> extraTexture0 = default,
        AssetRef<TextureDescriptor> extraTexture1 = default,
        AssetRef<TextureDescriptor> extraTexture2 = default,
        AssetRef<TextureDescriptor> extraTexture3 = default,
        int additionalTextureCount = 0)
        => snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.CreateTranslation(x, 0f),
                Color.White,
                layer,
                zIndex,
                flipX,
                flipY)
            {
                Bounds = bounds ?? Bounds.Empty,
                ShaderData = shader,
                Uniform0 = uniform0 ?? Vector4.Zero,
                Uniform1 = uniform1 ?? Vector4.Zero,
                Uniform2 = uniform2 ?? Vector4.Zero,
                Uniform3 = uniform3 ?? Vector4.Zero,
                ExtraTexture0 = extraTexture0,
                ExtraTexture1 = extraTexture1,
                ExtraTexture2 = extraTexture2,
                ExtraTexture3 = extraTexture3,
                AdditionalTextureCount = additionalTextureCount,
            });

    private static SpriteAsset Sprite(
        string? texture,
        Vector2? scale = null,
        Vector2[]? uvs = null,
        Vector2? pivot = null,
        SpriteTopology topology = SpriteTopology.Quad,
        ushort[]? indices = null)
        => new()
        {
            Texture = texture,
            Uvs = uvs ?? _fullUvs,
            Scale = scale ?? new Vector2(10f),
            Pivot = pivot ?? new Vector2(0.5f, 0.5f),
            Topology = topology,
            Indices = indices,
        };

    private sealed class PlanningFixture : IDisposable
    {
        private readonly ILoggerFactory _loggerFactory;

        internal PlanningFixture()
        {
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(Logs);
            });
            ILogger logger = _loggerFactory.CreateLogger("Bork2d.Render");
            Store = new TexturePageStore(Assets, Gpu, TexturePageCapacity.Initial, 16384, logger);
            BakedTexture placeholder = Ktx2TextureFactory.BakeSolid(
                TextureFormat.Rgba8,
                TexturePlaceholder.Size,
                TexturePlaceholder.Size);
            Store.Start(
                Ktx2TextureFactory.Descriptor(
                    TexturePlaceholder.PayloadPath,
                    placeholder,
                    TexturePlaceholder.Size,
                    TexturePlaceholder.Size,
                    TextureFilter.Nearest),
                placeholder.Payload.ToArray());
            Planner = new SpriteFramePlanner(Store, Shaders, logger);
        }

        internal FakeTextureAssets Assets { get; } = new();

        internal FakeTexturePageGpu Gpu { get; } = new();

        internal CapturingLoggerProvider Logs { get; } = new();

        internal TexturePageStore Store { get; }

        internal FakeShaderFrameSource Shaders { get; } = new();

        internal SpriteFramePlanner Planner { get; }

        internal FakeInstanceBuffer Instances { get; } = new();

        internal FakeGlobalUniformBuffer Globals { get; } = new();

        internal SpriteFramePlan Plan(FrameSnapshot snapshot, int width, int height)
            => Planner.Plan(
                snapshot,
                width,
                height,
                Instances,
                Globals,
                Format.R8G8B8A8Srgb);

        internal void MakeReadyPage(string path, uint width, uint height)
        {
            BakedTexture baked = Ktx2TextureFactory.BakeSolid(
                TextureFormat.Rgba8,
                (int)width,
                (int)height);
            TextureDescriptor descriptor = Ktx2TextureFactory.Descriptor(
                $"data/{path}",
                baked,
                width,
                height,
                TextureFilter.Nearest);
            Assets.CompleteDescriptor(path, descriptor);
            Assets.CompletePayload($"data/{path}", baked.Payload.ToArray());
            _ = Store.EnsurePage(path);
            Store.Pump();
        }

        internal void MakeFailedPage(string path, uint width, uint height)
        {
            var descriptor = new TextureDescriptor
            {
                Width = width,
                Height = height,
                Format = TextureFormat.Rgba8,
                Filter = TextureFilter.Nearest,
                Wrap = TextureWrap.Clamp,
                Payload = DataRef.None,
            };
            Assets.CompleteDescriptor(path, descriptor);
            _ = Store.EnsurePage(path);
            Store.Pump();
        }

        public void Dispose()
        {
            Store.Destroy();
            _loggerFactory.Dispose();
        }
    }
}
