using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Bork2d.Render.Textures;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Deterministic tests for the M8 sprite frame planner: sorting, masks, validity, page resolution,
/// quad math, camera skips, contiguous instance ranges, clears, throttled diagnostics, and the
/// allocation-free warm path (M8 §6.4–§6.8/§8.1).
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

        SpriteFramePlan plan = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

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

        SpriteFramePlan plan = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

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

        SpriteFramePlan plan = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

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

        SpriteFramePlan first = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);
        SpriteFramePlan second = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

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

        SpriteFramePlan plan = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

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

        SpriteFramePlan pending = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);
        Assert.Equal(1, pending.SkippedSprites);
        Assert.Equal(0, pending.InstanceCount);

        fixture.MakeReadyPage("tex/pending", 8, 8);
        SpriteFramePlan ready = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);
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
        AssertFatal(fixture, Sprite("tex/pending-bad", scale: 0f), "Scale");
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

        SpriteFramePlan validPending = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);
        Assert.Equal(1, validPending.SkippedSprites);
        Assert.Equal(0, validPending.InstanceCount);
    }

    [Fact]
    public void FailedTextureDrawsThePlaceholderWithItsDeclaredSizeAndAbsentTextureUsesEightByEight()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeFailedPage("tex/failed", 32, 16);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/failed", scale: 10f), layer: 1u, zIndex: 0);
        AddSprite(snapshot, Sprite(texture: null, scale: 10f), layer: 1u, zIndex: 1);

        SpriteFramePlan plan = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

        Assert.Equal(2, plan.InstanceCount);
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(3.2f, fixture.Instances.Get(0).Transform1.Z, 3);
        Assert.Equal(1.6f, fixture.Instances.Get(0).Transform1.W, 3);
        Assert.Equal(0.8f, fixture.Instances.Get(1).Transform1.Z, 3);
        Assert.Equal(0.8f, fixture.Instances.Get(1).Transform1.W, 3);
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
            Sprite("tex/a", scale: 8f, uvs: uvs, pivot: new Vector2(0.25f, 0.75f)),
            layer: 1u,
            flipX: true,
            flipY: true);

        _ = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

        SpriteInstance instance = fixture.Instances.Get(0);
        Assert.Equal(-0.5f, instance.Transform1.Z, 5);
        Assert.Equal(-0.8f, instance.Transform1.W, 5);
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

        SpriteFramePlan plan = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

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
    public void NonQuadTopologyIsSkippedWithOneDiagnosticPerAsset()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a", topology: SpriteTopology.TriangleStrip), layer: 1u);
        AddSprite(snapshot, Sprite("tex/a", topology: SpriteTopology.Triangles), layer: 1u);

        SpriteFramePlan first = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);
        SpriteFramePlan second = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

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
        AssertFatal(fixture, Sprite("tex/a", scale: 0f), "Scale");
        AssertFatal(fixture, Sprite("tex/a", scale: float.NaN), "Scale");
        AssertFatal(fixture, Sprite("tex/a", pivot: new Vector2(1.1f, 0.5f)), "Pivot");
        AssertFatal(fixture, Sprite("tex/a", topology: (SpriteTopology)7), "Topology");
        AssertFatal(fixture, Sprite("tex/a", indices: [0, 1, 2]), "Indices");
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
            AddSprite(snapshot, Sprite("tex/a"), layer: (uint)((index % 2) + 1), zIndex: index);
        }

        _ = fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            total += fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances).InstanceCount;
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
        SpriteAsset asset = Sprite("tex/a", scale: 8f);
        for (var index = 0; index < spriteCount; index++)
        {
            AddSprite(snapshot, asset, layer: 1u, zIndex: index, x: index * 0.01f);
        }

        SpriteFramePlan warm = fixture.Planner.Plan(snapshot, 1920, 1080, fixture.Instances);
        Assert.Equal(spriteCount, warm.InstanceCount);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var iteration = 0; iteration < 10; iteration++)
        {
            total += fixture.Planner.Plan(snapshot, 1920, 1080, fixture.Instances).InstanceCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(10 * spriteCount, total);
        Assert.True(
            fixture.Instances.InstanceCapacity >= spriteCount,
            "The warmed instance sink must hold the whole 50,000-sprite frame.");
    }

    private static void AssertFatal(PlanningFixture fixture, SpriteAsset asset, string field)
    {
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, asset, layer: 1u);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => fixture.Planner.Plan(snapshot, 320, 240, fixture.Instances));
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
        float x = 0f)
        => snapshot.AddSprite(
            new SpriteSnapshot(
                asset,
                Matrix3x2.CreateTranslation(x, 0f),
                Color.White,
                layer,
                zIndex,
                flipX,
                flipY));

    private static SpriteAsset Sprite(
        string? texture,
        float scale = 10f,
        Vector2[]? uvs = null,
        Vector2? pivot = null,
        SpriteTopology topology = SpriteTopology.Quad,
        ushort[]? indices = null)
        => new()
        {
            Texture = texture,
            Uvs = uvs ?? _fullUvs,
            Scale = scale,
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
            Store.Start();
            Planner = new SpriteFramePlanner(Store, logger);
        }

        internal FakeTextureAssets Assets { get; } = new();

        internal FakeTexturePageGpu Gpu { get; } = new();

        internal CapturingLoggerProvider Logs { get; } = new();

        internal TexturePageStore Store { get; }

        internal SpriteFramePlanner Planner { get; }

        internal FakeInstanceBuffer Instances { get; } = new();

        internal void MakeReadyPage(string path, uint width, uint height)
        {
            var descriptor = new TextureDescriptor
            {
                Width = width,
                Height = height,
                Format = TextureFormat.Rgba8,
                Filter = TextureFilter.Nearest,
                Wrap = TextureWrap.Clamp,
                Payload = $"data/{path}",
            };
            Assets.CompleteDescriptor(path, descriptor);
            Assets.CompletePayload($"data/{path}", new byte[width * height * 4]);
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
